using System.Data.SqlTypes;
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
using Shared.Persistence.Interfaces;
using Shared.Common.Pagination;
using Shared.Common.Billing;

namespace Billing.BLL.Implements;

public sealed class CashHandoverService(ICashHandoverRepository handovers, IPaymentRepository payments,
    IBillRepository bills, IBillCheckoutRepository checkouts, ICashRegisterRepository registers,
    ICashShiftRepository shifts, IPaymentMethodRepository methods, IUnitOfWork unitOfWork,
    IBillingAccess access, IPaymentRequestClient requests, IBillService billService,
    IPaymentService paymentService,
    IOptions<CashRegisterAccessSettings> roles, IOptions<CashPaymentSettings> settings, TimeProvider clock) : ICashHandoverService
{
    private const decimal MaximumMoney = 9999999999999999.99m;

    public async Task<(CashHandoverDTO Handover, bool Created)> Create(Guid billId, string key, CreateCashHandoverDTO request, CancellationToken ct)
    {
        if (billId == Guid.Empty || request is null || request.CheckoutId == Guid.Empty
            || !Guid.TryParse(key, out var requestId) || requestId == Guid.Empty
            || request.CashReceived is not decimal received || received < 0 || received > MaximumMoney || decimal.Truncate(received) != received)
        {
            throw new BillingException(400, "Cung cấp bill, checkout, Idempotency-Key UUID và toàn bộ tiền khách đưa bằng số nguyên VND.");
        }

        key = requestId.ToString("N");
        var initial = await bills.GetDetailsAsync(billId, ct) ?? throw new BillingException(404, "Không tìm thấy hóa đơn.");
        var sessions = initial.BillLines.Select(value => value.TableSessionRef).Distinct().ToArray();
        if (sessions.Length != 1 || sessions[0] == Guid.Empty)
        {
            throw new BillingException(409, "Hóa đơn phải thuộc đúng một phiên bàn.");
        }

        var sessionId = sessions[0];
        var actor = await access.GetCurrentScopeAsync(initial.RestaurantId, ct);
        RequireWaiter(actor, initial.RestaurantId);
        return await Controlled(async () => await unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            if (!await payments.LockIdempotencyKeyAsync(key, ct) || !await bills.LockSessionAsync(sessionId, ct))
            {
                throw new BillingException(409, "Ghi nhận thu hộ đang bận. Vui lòng thử lại cùng Idempotency-Key.");
            }

            var verified = await access.GetCurrentScopeAsync(initial.RestaurantId, ct);
            RequireWaiter(verified, initial.RestaurantId);
            if (verified.UserId != actor.UserId || await requests.GetReviewedWaiterAsync(sessionId, request.CheckoutId, ct) != actor.UserId)
            {
                throw new BillingException(403, "Chỉ phục vụ phụ trách yêu cầu được ghi nhận thu hộ.");
            }

            var checkout = await checkouts.GetByIdAsync(request.CheckoutId, ct);
            var active = await checkouts.GetActiveAsync(sessionId, ct);
            if (checkout is null || checkout.TableSessionRef != sessionId || checkout.BillId != billId
                || checkout.ReleasedAt.HasValue || active?.CheckoutId != checkout.CheckoutId)
            {
                throw new BillingException(409, "Checkout không thuộc hóa đơn hoặc đã được hủy.");
            }

            var existing = await payments.GetByKeyAsync(key, ct);
            if (existing is not null)
            {
                var saved = await handovers.GetByIdAsync(existing.PaymentId, ct);
                if (saved is null || existing.BillId != billId || saved.WaiterUserRef != actor.UserId || existing.CashReceived != received
                    || existing.CashShift.CashRegisterId != checkout.CashRegisterId)
                {
                    throw new BillingException(409, "Idempotency-Key đã dùng cho thông tin thu hộ khác.");
                }

                return (Map(saved), false);
            }

            if (checkout.Status != "Ready" || !checkout.CashRegisterId.HasValue || !checkout.PresentedAt.HasValue
                || !checkout.PreparedAt.HasValue || !checkout.PreparedByRef.HasValue || !checkout.SubmittedAt.HasValue
                || string.IsNullOrEmpty(checkout.SourceHash))
            {
                throw new BillingException(409, "Thu ngân phải chuẩn bị và phục vụ phải đưa hóa đơn cho khách trước khi thu hộ.");
            }

            var register = await registers.LockByIdAsync(checkout.CashRegisterId.Value, ct)
                ?? throw new BillingException(404, "Không tìm thấy quầy nhận.");
            if (register.RestaurantId != initial.RestaurantId)
            {
                throw new BillingException(403, "Quầy nhận không thuộc nhà hàng của hóa đơn.");
            }

            var open = await shifts.GetUnclosedAsync(register.CashRegisterId, ct);
            if (open.Count != 1)
            {
                throw new BillingException(409, "Quầy phải có đúng một ca đang mở.");
            }

            var shift = await shifts.LockByIdAsync(open[0].CashShiftId, ct);
            if (shift is null || shift.Status != "Active" || shift.ClosedAt.HasValue || shift.CashierUserRef == Guid.Empty)
            {
                throw new BillingException(409, "Ca nhận tiền không còn hoạt động.");
            }

            var bill = await bills.GetForRefreshAsync(billId, ct) ?? throw new BillingException(404, "Không tìm thấy hóa đơn.");
            if (bill.RestaurantId != register.RestaurantId || bill.Status != "Unpaid" || bill.PaidAt.HasValue
                || bill.Currency != "VND" || bill.TotalAmount <= 0 || bill.TotalAmount > MaximumMoney
                || decimal.Truncate(bill.TotalAmount) != bill.TotalAmount || bill.Payments.Any(PayOsPaymentRules.IsBlocking))
            {
                throw new BillingException(409, "Hóa đơn không còn đủ điều kiện thu hộ hoặc đã có giao dịch đang xử lý.");
            }

            if (received < bill.TotalAmount)
            {
                throw new BillingException(400, "Tiền khách đưa phải đủ tổng thanh toán.");
            }

            var method = await methods.LockByCodeAsync(settings.Value.MethodCode, ct);
            if (method is null || !method.IsEnabled)
            {
                throw new BillingException(409, "Phương thức tiền mặt chưa được cấu hình hoặc đã bị tắt.");
            }

            var prebill = await billService.GetPrebill(sessionId, request.CheckoutId, ct);
            if (prebill.SourceStatus != "Current" || prebill.TotalAmount != bill.TotalAmount)
            {
                throw new BillingException(409, "Hóa đơn chưa khớp dữ liệu đơn hàng hiện hành.");
            }

            var hash = CheckoutHash.Compute(prebill.Items.Select(line => new CheckoutHashLine(line.OrderItemRef,
                line.Item.ItemId, line.Quantity, line.Item.BaseUnitPrice, line.Item.Options.Select(option =>
                    new CheckoutHashOption(option.OptionId, option.Quantity, option.ExtraPrice)).ToArray())));
            if (hash != checkout.SourceHash)
            {
                throw new BillingException(409, "Hóa đơn không khớp tập món đã chốt. Chưa ghi nhận thu hộ.");
            }

            var payment = new Payment
            {
                PaymentId = Guid.NewGuid(),
                BillId = billId,
                Bill = bill,
                CashShiftId = shift.CashShiftId,
                CashShift = shift,
                MethodCode = method.MethodCode,
                Status = CashHandoverStatuses.PendingPayment,
                Amount = bill.TotalAmount,
                CashReceived = received,
                ChangeAmount = received - bill.TotalAmount,
                IdempotencyKey = key
            };
            var handover = new CashHandover
            {
                PaymentId = payment.PaymentId,
                Payment = payment,
                WaiterUserRef = actor.UserId,
                CollectedAt = Stamp(clock.GetUtcNow()),
                Status = CashHandoverStatuses.Pending
            };
            handovers.Add(handover);
            return (Map(handover), true);
        }, ct));
    }

    public async Task<CashHandoverDTO> GetById(Guid paymentId, CancellationToken ct)
    {
        var value = await Find(paymentId, ct);
        var actor = await access.GetCurrentScopeAsync(value.Payment.Bill.RestaurantId, ct);
        if (actor.UserId == Guid.Empty || (!actor.IsAdministrator && !actor.RestaurantIds.Contains(value.Payment.Bill.RestaurantId))
            || (!actor.IsAdministrator && !roles.Value.CashierRoleIds.Contains(actor.RoleId, StringComparer.Ordinal)
                && !roles.Value.ManagerRoleIds.Contains(actor.RoleId, StringComparer.Ordinal)
                && !(roles.Value.WaiterRoleIds.Contains(actor.RoleId, StringComparer.Ordinal) && actor.UserId == value.WaiterUserRef)))
        {
            throw new BillingException(403, "Không có quyền xem bản ghi thu hộ này.");
        }

        return Map(value);
    }

    public async Task<PagedResult<CashHandoverDTO>> GetAll(Guid registerId, int page, int pageSize, CancellationToken ct)
    {
        if (registerId == Guid.Empty || !new PaginationRequest
        {
            Page = page,
            PageSize = pageSize
        }.IsValid())
        {
            throw new BillingException(400, "Yêu cầu quầy và phân trang hợp lệ.");
        }

        var register = await registers.GetByIdAsync(registerId, ct) ?? throw new BillingException(404, "Không tìm thấy quầy thu ngân.");
        var actor = await access.GetCurrentScopeAsync(register.RestaurantId, ct);
        RequireCashier(actor, register.RestaurantId);
        var open = await shifts.GetUnclosedAsync(registerId, ct);
        if (open.Count != 1 || open[0].Status != "Active" || open[0].CashierUserRef != actor.UserId)
        {
            throw new BillingException(403, "Chỉ thu ngân sở hữu ca đang mở tại quầy nhận được xem danh sách bàn giao.");
        }

        var result = await handovers.GetPageAsync(open[0].CashShiftId, page, pageSize, ct);
        return new PagedResult<CashHandoverDTO>(result.Items.Select(Map).ToArray(), result.TotalCount, result.Page, result.PageSize);
    }

    public async Task<CashHandoverDTO> Confirm(Guid paymentId, decimal? confirmedAmount, CancellationToken ct)
    {
        var value = await Find(paymentId, ct);
        var actor = await access.GetCurrentScopeAsync(value.Payment.Bill.RestaurantId, ct);
        RequireCashier(actor, value.Payment.Bill.RestaurantId);
        if (value.Payment.CashShift.CashierUserRef != actor.UserId)
        {
            throw new BillingException(403, "Chỉ thu ngân sở hữu ca nhận được xác nhận bàn giao.");
        }

        var bill = await bills.GetDetailsAsync(value.Payment.BillId, ct) ?? throw new BillingException(404, "Không tìm thấy hóa đơn.");
        var sessions = bill.BillLines.Select(line => line.TableSessionRef).Distinct().ToArray();
        if (sessions.Length != 1)
        {
            throw new BillingException(409, "Không xác định được phiên bàn của hóa đơn.");
        }

        var checkout = await checkouts.GetActiveAsync(sessions[0], ct);
        if (checkout?.BillId != bill.BillId)
        {
            throw new BillingException(409, "Không xác định được checkout đang giữ hóa đơn.");
        }

        var confirmed = await paymentService.PayCash(bill.BillId, value.Payment.IdempotencyKey, new CreateCashPaymentDTO
        {
            CheckoutId = checkout.CheckoutId,
            CashShiftId = value.Payment.CashShiftId,
            CashReceived = confirmedAmount
        }, ct);
        var result = await GetById(paymentId, ct);
        result.SessionClosureConfirmed = confirmed.Payment.SessionClosureConfirmed;
        return result;
    }

    private async Task<CashHandover> Find(Guid id, CancellationToken ct)
    {
        if (id == Guid.Empty)
        {
            throw new BillingException(400, "Yêu cầu mã bàn giao hợp lệ.");
        }

        return await handovers.GetByIdAsync(id, ct) ?? throw new BillingException(404, "Không tìm thấy bản ghi thu hộ.");
    }

    private void RequireWaiter(BillingAccessScope actor, Guid restaurantId)
    {
        if (actor.UserId == Guid.Empty || actor.IsAdministrator || !actor.RestaurantIds.Contains(restaurantId)
            || !roles.Value.WaiterRoleIds.Contains(actor.RoleId, StringComparer.Ordinal))
        {
            throw new BillingException(403, "Chỉ phục vụ được phân công tại nhà hàng được ghi nhận thu hộ.");
        }
    }

    private void RequireCashier(BillingAccessScope actor, Guid restaurantId)
    {
        if (actor.UserId == Guid.Empty || actor.IsAdministrator || !actor.RestaurantIds.Contains(restaurantId)
            || !roles.Value.CashierRoleIds.Contains(actor.RoleId, StringComparer.Ordinal))
        {
            throw new BillingException(403, "Chỉ thu ngân được phân công tại nhà hàng được xác nhận nhận tiền.");
        }
    }

    private static async Task<T> Controlled<T>(Func<Task<T>> action)
    {
        try
        {
            return await action();
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new BillingException(409, "Dữ liệu thu hộ đã thay đổi; thử lại cùng mã yêu cầu.");
        }
        catch (DbUpdateException error) when (error.InnerException is SqlException sql && sql.Number is 1205 or 2601 or 2627)
        {
            throw new BillingException(409, "Ghi nhận thu hộ đang bận; thử lại cùng mã yêu cầu.");
        }
        catch (SqlException error) when (error.Number is 1205 or 1222)
        {
            throw new BillingException(409, "Ghi nhận thu hộ đang bận; thử lại cùng mã yêu cầu.");
        }
    }

    private static DateTime Stamp(DateTimeOffset value)
    {
        return DateTime.SpecifyKind(new SqlDateTime(value.UtcDateTime).Value, DateTimeKind.Utc);
    }

    private static CashHandoverDTO Map(CashHandover value)
    {
        return new CashHandoverDTO
        {
            PaymentId = value.PaymentId,
            BillId = value.Payment.BillId,
            RestaurantId = value.Payment.Bill.RestaurantId,
            CashRegisterId = value.Payment.CashShift.CashRegisterId,
            CashShiftId = value.Payment.CashShiftId,
            WaiterUserRef = value.WaiterUserRef,
            CashierUserRef = value.CashierUserRef,
            Amount = value.Payment.Amount,
            CashReceived = value.Payment.CashReceived ?? throw new BillingException(409, "Thiếu số tiền thu hộ đã lưu."),
            ChangeAmount = value.Payment.ChangeAmount ?? throw new BillingException(409, "Thiếu tiền thừa đã lưu."),
            ConfirmedAmount = value.ConfirmedAmount,
            Status = value.Status,
            PaymentStatus = value.Payment.Status,
            CollectedAt = new DateTimeOffset(DateTime.SpecifyKind(value.CollectedAt, DateTimeKind.Utc)),
            ConfirmedAt = value.ConfirmedAt.HasValue ? new DateTimeOffset(DateTime.SpecifyKind(value.ConfirmedAt.Value, DateTimeKind.Utc)) : null
        };
    }
}
