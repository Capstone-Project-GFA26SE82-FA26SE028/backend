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
using Shared.Common.Pagination;
using Shared.Persistence.Interfaces;

namespace Billing.BLL.Implements;

public sealed class CashHandoverService(ICashHandoverRepository handovers, IPaymentRepository payments,
    IBillRepository bills, IBillCheckoutRepository checkouts, ICashRegisterRepository registers,
    ICashShiftRepository shifts, IPaymentMethodRepository methods, IUnitOfWork unitOfWork,
    IBillingAccess access, IPaymentReviewClient reviews, IPaymentService paymentService,
    IOptions<CashRegisterAccessSettings> roles, IOptions<CashPaymentSettings> settings, TimeProvider clock,
    IPaidSessionClosureClient closure) : ICashHandoverService
{
    public async Task<(CashHandoverDTO Handover, bool Created)> Create(Guid billId, string key,
        CreateCashHandoverDTO request, CancellationToken ct)
    {
        if (billId == Guid.Empty || request is null || request.CheckoutId == Guid.Empty
            || !Guid.TryParse(key, out var requestId) || requestId == Guid.Empty
            || request.CashReceived is not decimal received || received <= 0
            || received > PaymentService.MaximumMoney || decimal.Truncate(received) != received)
        {
            throw new BillingException(400, "Cung cấp hóa đơn, checkout, Idempotency-Key UUID và toàn bộ tiền khách đưa bằng số nguyên VND.");
        }

        var actor = await access.GetCurrentScopeAsync(null, ct);
        RequireRole(actor, roles.Value.WaiterRoleIds, "Chỉ phục vụ được ghi nhận thu hộ.");
        var initial = await bills.GetDetailsAsync(billId, ct) ?? throw new BillingException(404, "Không tìm thấy hóa đơn.");
        RequireRestaurant(actor, initial.RestaurantId);
        var sessionId = GetSessionId(initial);
        key = requestId.ToString("N");

        try
        {
            return await unitOfWork.ExecuteInTransactionAsync(async () =>
            {
                if (!await payments.LockIdempotencyKeyAsync(key, ct) || !await bills.LockSessionAsync(sessionId, ct))
                {
                    throw new BillingException(409, "Thao tác thu hộ đang bận. Hãy thử lại cùng Idempotency-Key.");
                }

                var checkout = await checkouts.GetActiveAsync(sessionId, ct);
                if (checkout?.CheckoutId != request.CheckoutId || checkout.BillId != billId || !checkout.CashRegisterId.HasValue)
                {
                    throw new BillingException(409, "Yêu cầu thanh toán không thuộc hóa đơn hoặc đã hết hiệu lực.");
                }

                var existing = await payments.GetByKeyAsync(key, ct);
                if (existing is not null)
                {
                    if (existing.BillId != billId || existing.CashHandover is not { } saved
                        || saved.WaiterUserRef != actor.UserId || existing.CashReceived != received
                        || existing.CashShift.CashRegisterId != checkout.CashRegisterId)
                    {
                        throw new BillingException(409, "Idempotency-Key đã dùng cho thông tin thu hộ khác.");
                    }

                    return (Map(saved, existing), false);
                }

                if (checkout.Status != "Presented" || !checkout.PresentedAt.HasValue || !checkout.PreparedAt.HasValue
                    || !checkout.PreparedByRef.HasValue || !checkout.SubmittedAt.HasValue || string.IsNullOrEmpty(checkout.SourceHash))
                {
                    throw new BillingException(409, "Phục vụ phải xác nhận đã đưa hóa đơn cho khách trước khi ghi nhận thu hộ.");
                }

                var review = await reviews.GetReviewAsync(sessionId, checkout.CheckoutId, ct);
                if (review.RestaurantId != initial.RestaurantId)
                {
                    throw new BillingException(403, "Yêu cầu thanh toán không thuộc nhà hàng của hóa đơn.");
                }

                if (review.PendingOrderCount > 0)
                {
                    throw new BillingException(409, "Phiên bàn còn đơn chờ xác nhận. Chưa thể ghi nhận thu hộ.");
                }

                var register = await registers.LockByIdAsync(checkout.CashRegisterId.Value, ct)
                    ?? throw new BillingException(409, "Quầy thu ngân của yêu cầu không còn khả dụng.");
                if (register.RestaurantId != initial.RestaurantId)
                {
                    throw new BillingException(403, "Quầy nhận không thuộc nhà hàng của hóa đơn.");
                }

                var open = await shifts.GetUnclosedAsync(register.CashRegisterId, ct);
                var shift = open.Count == 1 ? await shifts.LockByIdAsync(open[0].CashShiftId, ct) : null;
                if (shift is null || shift.Status != CashShiftStatuses.Active
                    || shift.ClosedAt.HasValue || shift.CashierUserRef == Guid.Empty)
                {
                    throw new BillingException(409, "Quầy phải có đúng một ca thu ngân đang hoạt động để nhận tiền.");
                }

                var bill = await bills.GetForRefreshAsync(billId, ct) ?? throw new BillingException(404, "Không tìm thấy hóa đơn.");
                if (bill.RestaurantId != register.RestaurantId || bill.Status != "Unpaid" || bill.PaidAt.HasValue
                    || bill.Currency != "VND" || bill.TotalAmount <= 0 || bill.TotalAmount > PaymentService.MaximumMoney
                    || decimal.Truncate(bill.TotalAmount) != bill.TotalAmount
                    || bill.Payments.Any(payment => PayOsPaymentRules.IsBlocking(payment)
                        && !PayOsPaymentRules.BelongsToCheckout(payment, checkout.CheckoutId))
                    || GetSessionId(bill) != sessionId || BillCheckoutService.BillHash(BillService.Map(bill)) != checkout.SourceHash)
                {
                    throw new BillingException(409, "Hóa đơn không còn đủ điều kiện thu hộ, đã có giao dịch hoặc không khớp bản chụp món.");
                }

                if (received < bill.TotalAmount)
                {
                    throw new BillingException(400, "Tiền khách đưa phải đủ toàn bộ giá trị hóa đơn.");
                }

                if (!bill.Payments.Any(payment => PayOsPaymentRules.BelongsToCheckout(payment, checkout.CheckoutId)
                    && payment.Amount == bill.TotalAmount && PayOsPaymentRules.HasIssuedQr(payment)))
                {
                    throw new BillingException(409, "Cần tạo QR PayOS trên hóa đơn trước khi thu hộ.", "QrRequired");
                }

                var method = await methods.LockByCodeAsync(settings.Value.MethodCode, ct);
                if (method is null || !method.IsEnabled)
                {
                    throw new BillingException(409, "Phương thức tiền mặt chưa được cấu hình hoặc đã bị tắt.");
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
                    CollectedAt = PaymentService.CashTimestamp(clock.GetUtcNow()),
                    Status = CashHandoverStatuses.Pending
                };
                handovers.Add(handover);
                return (Map(handover, payment), true);
            }, ct);
        }
        catch (DbUpdateException error) when (error.InnerException is SqlException sql && sql.Number is 1205 or 2601 or 2627)
        {
            throw new BillingException(409, "Dữ liệu thu hộ đang được xử lý. Hãy thử lại cùng Idempotency-Key.");
        }
        catch (SqlException error) when (error.Number is 1205 or 1222)
        {
            throw new BillingException(409, "Thao tác thu hộ đang bận. Hãy thử lại cùng Idempotency-Key.");
        }
    }

    public async Task<CashHandoverDTO> GetById(Guid paymentId, CancellationToken ct)
    {
        var actor = await access.GetCurrentScopeAsync(null, ct);
        var value = await Find(paymentId, ct);
        RequireRestaurant(actor, value.Payment.Bill.RestaurantId);
        if (!actor.IsAdministrator && !roles.Value.CashierRoleIds.Contains(actor.RoleId, StringComparer.Ordinal)
            && !roles.Value.ManagerRoleIds.Contains(actor.RoleId, StringComparer.Ordinal)
            && !(roles.Value.WaiterRoleIds.Contains(actor.RoleId, StringComparer.Ordinal) && value.WaiterUserRef == actor.UserId))
        {
            throw new BillingException(403, "Không có quyền xem khoản thu hộ này.");
        }

        return Map(value, value.Payment);
    }

    public async Task<PagedResult<CashHandoverDTO>> GetAll(Guid registerId, int page, int pageSize, CancellationToken ct)
    {
        if (registerId == Guid.Empty || !new PaginationRequest
        {
            Page = page,
            PageSize = pageSize
        }.IsValid())
        {
            throw new BillingException(400, "Yêu cầu mã quầy và thông tin phân trang hợp lệ.");
        }

        var actor = await access.GetCurrentScopeAsync(null, ct);
        RequireRole(actor, roles.Value.CashierRoleIds, "Chỉ thu ngân đang đứng ca được xem danh sách bàn giao.");
        var register = await registers.GetByIdAsync(registerId, ct) ?? throw new BillingException(404, "Không tìm thấy quầy thu ngân.");
        RequireRestaurant(actor, register.RestaurantId);
        var open = await shifts.GetUnclosedAsync(registerId, ct);
        if (open.Count != 1 || open[0].Status != CashShiftStatuses.Active || open[0].CashierUserRef != actor.UserId)
        {
            throw new BillingException(403, "Chỉ thu ngân sở hữu ca đang mở được xem danh sách chờ bàn giao.");
        }

        var queue = await handovers.GetPageAsync(open[0].CashShiftId, page, pageSize, ct);
        return new PagedResult<CashHandoverDTO>(queue.Items.Select(value => Map(value, value.Payment)).ToArray(),
            queue.TotalCount, queue.Page, queue.PageSize);
    }

    public async Task<CashHandoverDTO> Confirm(Guid paymentId, decimal? confirmedAmount, CancellationToken ct)
    {
        var actor = await access.GetCurrentScopeAsync(null, ct);
        RequireRole(actor, roles.Value.CashierRoleIds, "Chỉ thu ngân được xác nhận nhận tiền.");
        var value = await Find(paymentId, ct);
        var payment = value.Payment;
        RequireRestaurant(actor, payment.Bill.RestaurantId);
        if (payment.CashShift.CashierUserRef != actor.UserId)
        {
            throw new BillingException(403, "Chỉ thu ngân sở hữu ca nhận tiền được xác nhận bàn giao.");
        }

        var bill = await bills.GetDetailsAsync(payment.BillId, ct) ?? throw new BillingException(404, "Không tìm thấy hóa đơn.");
        var sessionId = GetSessionId(bill);
        var checkout = await checkouts.GetActiveAsync(sessionId, ct);
        if (checkout?.BillId != payment.BillId)
        {
            throw new BillingException(409, "Không xác định được yêu cầu thanh toán của hóa đơn.");
        }

        await paymentService.ConfirmCashHandover(paymentId, payment.BillId, payment.IdempotencyKey, new CreateCashPaymentDTO
        {
            CheckoutId = checkout.CheckoutId,
            CashShiftId = payment.CashShiftId,
            CashReceived = confirmedAmount
        }, ct);
        await closure.TryCloseAsync(bill.RestaurantId, sessionId, ct);
        var confirmed = await Find(paymentId, ct);
        return Map(confirmed, confirmed.Payment);
    }

    private async Task<CashHandover> Find(Guid paymentId, CancellationToken ct)
    {
        if (paymentId == Guid.Empty)
        {
            throw new BillingException(400, "Yêu cầu mã bàn giao hợp lệ.");
        }

        return await handovers.GetByIdAsync(paymentId, ct) ?? throw new BillingException(404, "Không tìm thấy khoản thu hộ.");
    }

    private static Guid GetSessionId(Bill bill)
    {
        var sessions = bill.BillLines.Select(line => line.TableSessionRef).Distinct().ToArray();
        if (sessions.Length != 1 || sessions[0] == Guid.Empty)
        {
            throw new BillingException(409, "Hóa đơn phải thuộc đúng một phiên bàn.");
        }

        return sessions[0];
    }

    private static void RequireRole(BillingAccessScope actor, IReadOnlyList<string> allowed, string message)
    {
        if (actor.UserId == Guid.Empty)
        {
            throw new BillingException(401, "Yêu cầu phiên đăng nhập nhân viên hợp lệ.");
        }

        if (actor.IsAdministrator || !allowed.Contains(actor.RoleId, StringComparer.Ordinal))
        {
            throw new BillingException(403, message);
        }
    }

    private static void RequireRestaurant(BillingAccessScope actor, Guid restaurantId)
    {
        if (actor.UserId == Guid.Empty || (!actor.IsAdministrator && !actor.RestaurantIds.Contains(restaurantId)))
        {
            throw new BillingException(403, "Yêu cầu phân công đang hoạt động tại nhà hàng này.");
        }
    }

    private static CashHandoverDTO Map(CashHandover handover, Payment payment)
    {
        return new CashHandoverDTO
        {
            PaymentId = payment.PaymentId,
            BillId = payment.BillId,
            RestaurantId = payment.Bill.RestaurantId,
            CashRegisterId = payment.CashShift.CashRegisterId,
            CashShiftId = payment.CashShiftId,
            WaiterUserRef = handover.WaiterUserRef,
            CashierUserRef = handover.CashierUserRef,
            Amount = payment.Amount,
            CashReceived = payment.CashReceived ?? throw new BillingException(409, "Thiếu số tiền thu hộ đã lưu."),
            ChangeAmount = payment.ChangeAmount ?? throw new BillingException(409, "Thiếu tiền thừa đã lưu."),
            ConfirmedAmount = handover.ConfirmedAmount,
            Status = handover.Status,
            PaymentStatus = payment.Status,
            CollectedAt = new DateTimeOffset(DateTime.SpecifyKind(handover.CollectedAt, DateTimeKind.Utc)),
            ConfirmedAt = handover.ConfirmedAt is DateTime confirmedAt
                ? new DateTimeOffset(DateTime.SpecifyKind(confirmedAt, DateTimeKind.Utc))
                : null
        };
    }
}
