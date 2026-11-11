using System.Data.SqlTypes;
using System.Globalization;
using System.Text.Json;
using Billing.BLL.Interfaces;
using Billing.Common.Constants;
using Billing.Common.DTOs;
using Billing.Common.Exceptions;
using Billing.Common.Settings;
using Billing.DAL.Interfaces;
using Billing.DAL.Models;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Shared.Common.Billing;
using Shared.Persistence.Interfaces;

namespace Billing.BLL.Implements;

public sealed partial class PayOsPaymentService(IPaymentRepository payments, IBillRepository bills,
    IBillCheckoutRepository checkouts, ICashShiftRepository shifts, ICashRegisterRepository registers,
    IPaymentMethodRepository methods, IPaymentWebhookRepository webhooks, IUnitOfWork unitOfWork,
    IBillingAccess access, IBillService billService, ICheckoutSourceClient ordering, IPayOsClient provider,
    IOptions<CashRegisterAccessSettings> roles, IOptions<PayOsSettings> settings, TimeProvider clock) : IPayOsPaymentService
{
    public async Task<(PayOsPaymentDTO Payment, bool Created)> Create(Guid billId, string key, CreatePayOsPaymentDTO request, CancellationToken ct)
    {
        var actor = await access.GetCurrentScopeAsync(null, ct);
        RequireRole(actor, true);
        if (billId == Guid.Empty || request is null || request.CashShiftId == Guid.Empty || request.CheckoutId == Guid.Empty
            || !Guid.TryParse(key, out var requestId) || requestId == Guid.Empty)
        {
            throw new BillingException(400, "Vui lòng cung cấp mã hóa đơn, checkout, ca làm việc và Idempotency-Key UUID.");
        }

        key = PayOsStates.KeyPrefix + requestId.ToString("N");
        var initial = await bills.GetDetailsAsync(billId, ct) ?? throw new BillingException(404, "Không tìm thấy hóa đơn.");
        RequireBranch(actor, initial.RestaurantId);
        var sessionId = Session(initial);
        var prepared = await Controlled(async () => await unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            if (!await payments.LockIdempotencyKeyAsync(key, ct) || !await bills.LockSessionAsync(sessionId, ct))
            {
                throw new BillingException(409, "Chuẩn bị thanh toán đang bận. Vui lòng thử lại cùng key.");
            }

            var info = await shifts.GetDetailsAsync(request.CashShiftId, ct) ?? throw new BillingException(404, "Không tìm thấy ca thu ngân.");
            var register = await registers.LockByIdAsync(info.CashRegisterId, ct) ?? throw new BillingException(404, "Không tìm thấy quầy thu ngân.");
            var shift = await shifts.LockByIdAsync(request.CashShiftId, ct) ?? throw new BillingException(404, "Không tìm thấy ca thu ngân.");
            var verified = await access.GetCurrentScopeAsync(initial.RestaurantId, ct);
            RequireRole(verified, true);
            RequireBranch(verified, initial.RestaurantId);
            if (verified.UserId != actor.UserId || shift.CashierUserRef != verified.UserId || register.RestaurantId != initial.RestaurantId)
            {
                throw new BillingException(403, "Vui lòng dùng ca làm việc của chính bạn tại nhà hàng của hóa đơn.");
            }

            var matches = await payments.GetByPayOsRequestKeyAsync(key, ct);
            if (matches.Count > 1)
            {
                throw new BillingException(409, "Bản ghi yêu cầu thanh toán không nhất quán và cần được kiểm tra lại.");
            }

            var previous = matches.SingleOrDefault();
            var checkout = await checkouts.GetByIdAsync(request.CheckoutId, ct);
            if (previous is not null)
            {
                if (previous.IdempotencyKey != key + ":" + request.CheckoutId.ToString("N")
                    || previous.BillId != billId || previous.CashShiftId != request.CashShiftId || !PayOsPaymentRules.IsPayOs(previous)
                    || checkout?.BillId != billId || checkout.TableSessionRef != sessionId)
                {
                    throw new BillingException(409, "Idempotency-Key đã được sử dụng cho một yêu cầu thanh toán khác.");
                }

                return (previous.PaymentId, false);
            }

            if (!settings.Value.Enabled)
            {
                throw new BillingException(409, "Cổng PayOS đang bị tắt. Vui lòng cấu hình và bật PayOS trên máy chủ.");
            }

            provider.EnsureCanCreate();
            var unclosed = await shifts.GetUnclosedAsync(register.CashRegisterId, ct);
            var active = await checkouts.GetActiveAsync(sessionId, ct);
            if (shift.Status != CashShiftStatuses.Active || shift.ClosedAt.HasValue || unclosed.Count != 1
                || unclosed[0].CashShiftId != shift.CashShiftId || checkout?.Status != "Ready"
                || checkout.BillId != billId || checkout.TableSessionRef != sessionId || checkout.ReleasedAt.HasValue
                || active?.CheckoutId != checkout.CheckoutId)
            {
                throw new BillingException(409, "Yêu cầu trạng thái checkout Ready và ca làm việc duy nhất đang mở của bạn.");
            }

            if (checkout.CashRegisterId != register.CashRegisterId || !checkout.SubmittedAt.HasValue
                || !checkout.PreparedByRef.HasValue || !checkout.PreparedAt.HasValue || !checkout.PresentedAt.HasValue)
            {
                throw new BillingException(409, "Phải dùng đúng quầy nhận và phục vụ đã xác nhận đưa hóa đơn cho khách trước khi thanh toán.");
            }

            var bill = await bills.GetForRefreshAsync(billId, ct) ?? throw new BillingException(404, "Không tìm thấy hóa đơn.");
            if (bill.Status != "Unpaid" || bill.PaidAt.HasValue || bill.Payments.Any(PayOsPaymentRules.IsBlocking)
                || bill.Currency != "VND" || bill.TotalAmount <= 0 || bill.TotalAmount > 9999999999999999.99m
                || decimal.Truncate(bill.TotalAmount) != bill.TotalAmount)
            {
                throw new BillingException(409, "Hóa đơn phải chưa thanh toán, tổng tiền nguyên dương VND và chưa có giao dịch đang xử lý.");
            }

            var method = await methods.LockByCodeAsync(PayOsStates.Method, ct);
            if (method is null || !method.IsEnabled)
            {
                throw new BillingException(409, "Phương thức chuyển khoản (BankTransfer) chưa được cấu hình hoặc đã bị tắt.");
            }

            var current = await billService.GetById(billId, ct);
            if (current.SourceStatus != "Current" || Hash(bill) != checkout.SourceHash)
            {
                throw new BillingException(409, "Hóa đơn không khớp với bản chụp đã khóa của gọi món. Không thể tạo yêu cầu PayOS.");
            }

            await ordering.BindAsync(sessionId, checkout.CheckoutId, billId, checkout.SourceHash, ct);
            DateTime expires;
            try
            {
                expires = Stamp(clock.GetUtcNow().AddMinutes(settings.Value.LinkLifetimeMinutes));
                if (new DateTimeOffset(expires).ToUnixTimeSeconds() > int.MaxValue)
                {
                    throw new ArgumentOutOfRangeException();
                }
            }
            catch (ArgumentOutOfRangeException)
            {
                throw new BillingException(409, "Cần cấu hình thời hạn hiệu lực liên kết PayOS hợp lệ.");
            }

            var payment = new Payment
            {
                PaymentId = Guid.NewGuid(),
                BillId = billId,
                CashShiftId = shift.CashShiftId,
                MethodCode = method.MethodCode,
                Status = PayOsStates.Creating,
                Amount = bill.TotalAmount,
                IdempotencyKey = key + ":" + request.CheckoutId.ToString("N"),
                ProviderOrderCode = await payments.AllocateOrderCodeAsync(ct),
                ExpiredAt = expires
            };
            await payments.AddAsync(payment);
            return (payment.PaymentId, true);
        }, ct));
        var stored = await payments.GetDetailsAsync(prepared.Item1, ct) ?? throw new BillingException(404, "Không tìm thấy khoản thanh toán.");
        if (!prepared.Item2 && (stored.Status is PaymentStatuses.Paid or PayOsStates.ReviewRequired || PayOsPaymentRules.IsVerifiedZero(stored)))
        {
            return (Map(stored), false);
        }

        var result = await Synchronize(prepared.Item1, ct);
        return (result, prepared.Item2);
    }

    public async Task<PayOsPaymentDTO> GetById(Guid id, CancellationToken ct)
    {
        return Map(await Authorized(id, false, ct));
    }

    public async Task<PayOsPaymentDTO> Reconcile(Guid id, CancellationToken ct)
    {
        await Authorized(id, true, ct);
        return await Synchronize(id, ct);
    }

    public async Task<PayOsPaymentDTO> Cancel(Guid id, CancellationToken ct)
    {
        await Authorized(id, true, ct);
        provider.EnsureConfigured();
        payments.ResetProviderTracking();
        await Controlled(async () => await unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            if (!await payments.LockProviderWorkAsync(id, ct))
            {
                throw new BillingException(409, "PayOS đang được đối soát. Vui lòng thử lại việc hủy cho cùng giao dịch.");
            }

            var initial = await payments.GetDetailsAsync(id, ct) ?? throw new BillingException(404, "Không tìm thấy khoản thanh toán.");
            var bill = await bills.GetDetailsAsync(initial.BillId, ct) ?? throw new BillingException(404, "Không tìm thấy hóa đơn.");
            await LockExisting(initial, Session(bill), ct);
            await Authorized(id, true, ct);
            var payment = await payments.GetForProviderUpdateAsync(id, ct) ?? throw new BillingException(404, "Không tìm thấy khoản thanh toán.");
            if (payment.Status == PaymentStatuses.Paid || payment.PaidAt.HasValue || payment.Status == PayOsStates.ReviewRequired)
            {
                throw new BillingException(409, "Giao dịch đã thanh toán hoặc cần kiểm tra không thể hủy qua quy trình này.");
            }

            if (!PayOsPaymentRules.IsVerifiedZero(payment))
            {
                payment.Status = PayOsStates.CancelRequested;
            }

            return true;
        }, ct));
        return await Synchronize(id, ct);
    }

    private async Task<Payment> Authorized(Guid id, bool write, CancellationToken ct)
    {
        var actor = await access.GetCurrentScopeAsync(null, ct);
        RequireRole(actor, write);
        if (id == Guid.Empty)
        {
            throw new BillingException(400, "Yêu cầu ID thanh toán hợp lệ.");
        }

        var payment = await payments.GetDetailsAsync(id, ct) ?? throw new BillingException(404, "Không tìm thấy khoản thanh toán.");
        RequireBranch(actor, payment.Bill.RestaurantId);
        if (!PayOsPaymentRules.IsPayOs(payment))
        {
            throw new BillingException(404, "Không tìm thấy giao dịch PayOS.");
        }

        if (write && actor.UserId != payment.CashShift.CashierUserRef)
        {
            throw new BillingException(403, "Chỉ thu ngân sở hữu mới có thể quản lý giao dịch PayOS này.");
        }

        return payment;
    }

    private void RequireRole(BillingAccessScope actor, bool write)
    {
        if (actor.UserId == Guid.Empty)
        {
            throw new BillingException(401, "Yêu cầu phiên đăng nhập nhân viên hợp lệ.");
        }

        var cashier = roles.Value.CashierRoleIds.Contains(actor.RoleId, StringComparer.Ordinal);
        var manager = roles.Value.ManagerRoleIds.Contains(actor.RoleId, StringComparer.Ordinal);
        if (write ? actor.IsAdministrator || !cashier : !actor.IsAdministrator && !cashier && !manager)
        {
            throw new BillingException(403, write ? "Chỉ thu ngân mới có quyền quản lý yêu cầu thanh toán PayOS." : "Yêu cầu quyền xem thông tin thanh toán.");
        }
    }

    private static void RequireBranch(BillingAccessScope actor, Guid restaurantId)
    {
        if (!actor.IsAdministrator && !actor.RestaurantIds.Contains(restaurantId))
        {
            throw new BillingException(403, "Yêu cầu phân công nhà hàng đang hoạt động.");
        }
    }

    private async Task LockExisting(Payment value, Guid sessionId, CancellationToken ct)
    {
        if (!await bills.LockSessionAsync(sessionId, ct))
        {
            throw new BillingException(409, "Checkout đang bận. Vui lòng thử lại cùng thanh toán.");
        }

        await registers.LockByIdAsync(value.CashShift.CashRegisterId, ct);
        await shifts.LockByIdAsync(value.CashShiftId, ct);
        await bills.GetForRefreshAsync(value.BillId, ct);
    }

    private static Guid Session(Bill value)
    {
        var ids = value.BillLines.Select(line => line.TableSessionRef).Distinct().ToArray();
        if (ids.Length != 1 || ids[0] == Guid.Empty)
        {
            throw new BillingException(409, "Thanh toán yêu cầu hóa đơn của một phiên bàn có thể xác minh.");
        }

        return ids[0];
    }

    private static string Hash(Bill value)
    {
        try
        {
            return CheckoutHash.Compute(value.BillLines.Select(line =>
            {
                var item = JsonSerializer.Deserialize<BillItemSnapshot>(line.ItemSnapshot) ?? throw new JsonException();
                return new CheckoutHashLine(line.OrderItemRef, item.ItemId, line.Quantity, item.BaseUnitPrice,
                    item.Options.Select(option => new CheckoutHashOption(option.OptionId, option.Quantity, option.ExtraPrice)).ToArray());
            }));
        }
        catch (JsonException)
        {
            throw new BillingException(409, "Bản chụp hóa đơn đã lưu không thể xác minh.");
        }
    }

    private static DateTime Stamp(DateTimeOffset value)
    {
        return DateTime.SpecifyKind(new SqlDateTime(value.UtcDateTime).Value, DateTimeKind.Utc);
    }

    private static PayOsPaymentDTO Map(Payment value)
    {
        return new PayOsPaymentDTO
        {
            PaymentId = value.PaymentId,
            BillId = value.BillId,
            CashShiftId = value.CashShiftId,
            RestaurantId = value.Bill.RestaurantId,
            Status = value.Status,
            Amount = value.Amount,
            ProviderOrderCode = value.ProviderOrderCode,
            ProviderPaymentLinkId = value.ProviderPaymentLinkId,
            CheckoutUrl = value.ProviderCheckoutUrl,
            QrCode = value.ProviderQrCode,
            ProviderTransactionId = value.ProviderTransactionId,
            ExpiresAt = value.ExpiredAt.HasValue ? new DateTimeOffset(DateTime.SpecifyKind(value.ExpiredAt.Value, DateTimeKind.Utc)) : null,
            PaidAt = value.PaidAt.HasValue ? new DateTimeOffset(DateTime.SpecifyKind(value.PaidAt.Value, DateTimeKind.Utc)) : null,
            IsVerifiedZero = PayOsPaymentRules.IsVerifiedZero(value),
            RequiresReview = value.Status == PayOsStates.ReviewRequired,
            LinkDetailsAvailable = !string.IsNullOrEmpty(value.ProviderCheckoutUrl) && !string.IsNullOrEmpty(value.ProviderQrCode)
        };
    }

    private static async Task<T> Controlled<T>(Func<Task<T>> action)
    {
        try
        {
            return await action();
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new BillingException(409, "Thanh toán đã thay đổi. Vui lòng thử lại cùng yêu cầu thay vì tạo mới.");
        }
        catch (DbUpdateException error) when (error.InnerException is SqlException sql && sql.Number is 1205 or 2601 or 2627)
        {
            throw new BillingException(409, "Thanh toán đang bận. Vui lòng thử lại cùng yêu cầu.");
        }
        catch (SqlException error) when (error.Number is 1205 or 1222)
        {
            throw new BillingException(409, "Thanh toán đang bận. Vui lòng thử lại cùng yêu cầu.");
        }
    }
}
