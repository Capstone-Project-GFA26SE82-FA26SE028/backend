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

namespace Billing.BLL.Implements;

public sealed class PaymentService(IPaymentRepository payments, IBillRepository bills,
    IBillCheckoutRepository checkouts, ICashShiftRepository shifts, ICashRegisterRepository registers,
    ICashMovementRepository movements, IUnitOfWork unitOfWork,
    IBillingAccess access, IBillService billService, ICheckoutSourceClient ordering,
    IOptions<CashRegisterAccessSettings> roles, IOptions<CashPaymentSettings> settings, TimeProvider clock,
    IVoucherRedemptionRepository? voucherRedemptions = null,
    ICustomerLoyaltyClient? loyaltyClient = null,
    IPayOsPaymentService? payOs = null, IPaymentMethodRepository? methods = null,
    IPaidSessionClosureClient? closure = null) : IPaymentService
{
    internal const decimal MaximumMoney = 9999999999999999.99m;

    public async Task<(PaymentDTO Payment, bool Created)> PayCash(Guid billId, string key,
        CreateCashPaymentDTO request, CancellationToken ct)
    {
        var result = await SettleCash(billId, key, request, null, ct);
        if (closure is not null)
        {
            var bill = await bills.GetDetailsAsync(billId, ct);
            var sessionId = bill?.BillLines.FirstOrDefault()?.TableSessionRef;
            if (sessionId.HasValue)
            {
                await closure.TryCloseAsync(result.Payment.RestaurantId, sessionId.Value, ct);
            }
        }

        return result;
    }

    public Task<(PaymentDTO Payment, bool Created)> ConfirmCashHandover(Guid paymentId, Guid billId, string key,
        CreateCashPaymentDTO request, CancellationToken ct)
    {
        return SettleCash(billId, key, request, paymentId, ct);
    }

    private async Task<(PaymentDTO Payment, bool Created)> SettleCash(Guid billId, string key,
        CreateCashPaymentDTO request, Guid? handoverPaymentId, CancellationToken ct)
    {
        try
        {
            return await SettleCashCore(billId, key, request, handoverPaymentId, ct);
        }
        catch (OpenPayOsException open)
        {
            if (payOs is not null)
            {
                try
                {
                    await payOs.Cancel(open.PaymentId, ct);
                }
                catch (BillingException error) when (error.StatusCode is 409 or 502 or 503 or 504)
                {
                    // A concurrent webhook or an uncertain cancel is resolved from the latest persisted state below.
                }

                payments.ResetProviderTracking();
                try
                {
                    return await SettleCashCore(billId, key, request, handoverPaymentId, ct);
                }
                catch (OpenPayOsException)
                {
                    // A verified terminal state is required before posting cash.
                }
            }

            throw new BillingException(409, "Chờ đối soát PayOS. Chưa ghi nhận tiền mặt.", "PendingReconciliation");
        }
    }

    private async Task<(PaymentDTO Payment, bool Created)> SettleCashCore(Guid billId, string key,
        CreateCashPaymentDTO request, Guid? handoverPaymentId, CancellationToken ct)
    {
        var actor = await access.GetCurrentScopeAsync(null, ct);
        RequireRole(actor, true);
        if (billId == Guid.Empty || request is null || request.CheckoutId == Guid.Empty
            || request.CashShiftId == Guid.Empty || !Guid.TryParse(key, out var requestId) || requestId == Guid.Empty
            || request.CashReceived is not decimal received || received < 0 || received > MaximumMoney
            || decimal.Truncate(received) != received)
        {
            throw new BillingException(400, "Vui lòng cung cấp mã hóa đơn, checkout, ca làm việc hợp lệ, Idempotency-Key UUID và số tiền mặt VND nguyên.");
        }

        key = requestId.ToString("N");
        var initial = await bills.GetDetailsAsync(billId, ct) ?? throw new BillingException(404, "Không tìm thấy hóa đơn.");
        RequireBranch(actor, initial.RestaurantId);
        var sessions = initial.BillLines.Select(line => line.TableSessionRef).Distinct().ToArray();
        if (sessions.Length != 1 || sessions[0] == Guid.Empty)
        {
            throw new BillingException(409, "Hóa đơn phải xác định đúng một phiên bàn trước khi thanh toán.");
        }

        try
        {
            return await unitOfWork.ExecuteInTransactionAsync(async () =>
            {
                if (!await payments.LockIdempotencyKeyAsync(key, ct) || !await bills.LockSessionAsync(sessions[0], ct))
                {
                    throw new BillingException(409, "Thanh toán đang bận. Vui lòng thử lại cùng Idempotency-Key.");
                }

                var verified = await access.GetCurrentScopeAsync(initial.RestaurantId, ct);
                RequireRole(verified, true);
                RequireBranch(verified, initial.RestaurantId);
                if (actor.UserId != verified.UserId)
                {
                    throw new BillingException(401, "Phiên đăng nhập nhân viên đã thay đổi. Vui lòng đăng nhập lại.");
                }

                var checkout = await checkouts.GetByIdAsync(request.CheckoutId, ct);
                var active = await checkouts.GetActiveAsync(sessions[0], ct);
                if (checkout is null || checkout.TableSessionRef != sessions[0] || checkout.BillId != billId
                    || checkout.ReleasedAt.HasValue || active?.CheckoutId != checkout.CheckoutId)
                {
                    throw new BillingException(409, "Hóa đơn này không có yêu cầu thanh toán đang hoạt động như yêu cầu.");
                }

                var existing = await payments.GetByKeyAsync(key, ct);
                if ((handoverPaymentId.HasValue && existing?.PaymentId != handoverPaymentId.Value)
                    || (!handoverPaymentId.HasValue && existing?.CashHandover is not null))
                {
                    throw new BillingException(409, "Dùng đúng yêu cầu xác nhận của khoản tiền mặt đã ghi nhận.");
                }

                if (existing is not null && (existing.BillId != billId || existing.CashShiftId != request.CashShiftId
                    || existing.CashReceived != received || existing.MethodCode != settings.Value.MethodCode))
                {
                    throw new BillingException(409, "Toàn bộ số tiền xác nhận phải khớp với khoản phục vụ đã ghi nhận.");
                }

                if (existing?.Status == PaymentStatuses.Paid)
                {
                    if (existing.ConfirmedByRef != verified.UserId)
                    {
                        throw new BillingException(403, "Chỉ thu ngân đã nhận tiền được gửi lại yêu cầu xác nhận.");
                    }

                    if (checkout.Status != PaymentStatuses.Paid || existing.Bill.Status != PaymentStatuses.Paid
                        || (handoverPaymentId.HasValue && !IsConfirmedHandover(existing)))
                    {
                        throw new BillingException(409, "Thanh toán đã lưu không phải là biên lai tiền mặt hoàn tất. Hãy kiểm tra trước khi thử lại.");
                    }

                    return (Map(existing, checkout.CheckoutId), false);
                }

                if (checkout.Status is not ("Presented" or PaymentStatuses.Paid) || !PayOsPaymentRules.HasPresentedBill(checkout))
                {
                    throw new BillingException(409, "Hãy giao hóa đơn để khách kiểm tra trước khi xác nhận tiền mặt.");
                }

                var shiftInfo = await shifts.GetDetailsAsync(request.CashShiftId, ct)
                    ?? throw new BillingException(404, "Không tìm thấy ca làm việc tiền mặt.");
                RequireBranch(verified, shiftInfo.CashRegister.RestaurantId);
                if (shiftInfo.CashRegister.RestaurantId != initial.RestaurantId
                    || shiftInfo.CashRegisterId != checkout.CashRegisterId)
                {
                    throw new BillingException(409, "Ca làm việc tiền mặt và hóa đơn phải thuộc cùng một nhà hàng.");
                }

                var register = await registers.LockByIdAsync(shiftInfo.CashRegisterId, ct)
                    ?? throw new BillingException(404, "Không tìm thấy máy thu ngân.");
                var shift = await shifts.LockByIdAsync(request.CashShiftId, ct)
                    ?? throw new BillingException(404, "Không tìm thấy ca làm việc tiền mặt.");
                var unclosed = await shifts.GetUnclosedAsync(register.CashRegisterId, ct);
                if (shift.CashierUserRef != verified.UserId)
                {
                    throw new BillingException(403, "Thu ngân chỉ có thể thu tiền trong ca làm việc của chính mình.");
                }

                var bill = await bills.GetForRefreshAsync(billId, ct) ?? throw new BillingException(404, "Không tìm thấy hóa đơn.");
                var payOsAttempts = bill.Payments.Where(value => PayOsPaymentRules.BelongsToCheckout(value, checkout.CheckoutId)).ToArray();
                if (bill.RestaurantId == register.RestaurantId && payOsAttempts.Any(PayOsPaymentRules.IsVerifiedPaid))
                {
                    throw new BillingException(409, "Hóa đơn đã thanh toán qua PayOS. Trả lại toàn bộ tiền mặt đã nhận.",
                        "CashReturnRequired", received);
                }

                if (shift.ClosedAt.HasValue || shift.Status != CashShiftStatuses.Active
                    || unclosed.Count != 1 || unclosed[0].CashShiftId != shift.CashShiftId)
                {
                    throw new BillingException(409, "Ca làm việc đã chọn không phải là ca duy nhất đang hoạt động của máy thu ngân này.");
                }

                if (bill.RestaurantId != register.RestaurantId || bill.Status != "Unpaid" || bill.PaidAt.HasValue
                    || bill.Payments.Any(value => value.PaymentId != existing?.PaymentId && PayOsPaymentRules.IsBlocking(value)
                        && !PayOsPaymentRules.BelongsToCheckout(value, checkout.CheckoutId))
                    || bill.Currency != "VND" || bill.TotalAmount <= 0
                    || bill.TotalAmount > MaximumMoney || decimal.Truncate(bill.TotalAmount) != bill.TotalAmount)
                {
                    throw new BillingException(409, "Chỉ hóa đơn chưa thanh toán, số tiền nguyên dương VND và chưa có giao dịch mới có thể thanh toán tiền mặt.");
                }

                if (received < bill.TotalAmount)
                {
                    throw new BillingException(400, "Tiền khách đưa chưa đủ thanh toán hóa đơn.",
                        "InsufficientCash", bill.TotalAmount - received);
                }

                var payment = bill.Payments.SingleOrDefault(value => value.PaymentId == existing?.PaymentId);
                var handover = payment?.CashHandover;
                if (handoverPaymentId.HasValue && (payment is null || !PayOsPaymentRules.IsPendingCashHandover(payment)
                    || handover!.WaiterUserRef == Guid.Empty || payment.Amount != bill.TotalAmount || payment.CashReceived != received))
                {
                    throw new BillingException(409, "Bản ghi thu hộ không còn chờ bàn giao hoặc không khớp với hóa đơn.");
                }

                if (!handoverPaymentId.HasValue && (existing is not null || methods is null
                    || (await methods.LockByCodeAsync(settings.Value.MethodCode, ct))?.IsEnabled != true))
                {
                    throw new BillingException(409, "Không thể tạo khoản thu tiền mặt trực tiếp với yêu cầu này.");
                }

                var current = await billService.GetById(billId, ct);
                if (current.SourceStatus != "Current")
                {
                    throw new BillingException(current.SourceStatus == "Unavailable" ? 503 : 409,
                        "Nguồn hóa đơn không còn cập nhật. Chưa ghi nhận thanh toán; vui lòng kiểm tra lại đơn trước khi thu tiền mặt.");
                }

                var hash = BillCheckoutService.BillHash(current);
                if (hash != checkout.SourceHash)
                {
                    throw new BillingException(409, "Hóa đơn không khớp với danh sách món sẵn sàng thanh toán.");
                }

                await ordering.BindAsync(sessions[0], checkout.CheckoutId, billId, hash, ct);
                var expectedCash = shift.OpeningCash + await movements.GetTotalAsync(shift.CashShiftId, ct);
                if (expectedCash < 0 || expectedCash > MaximumMoney || expectedCash + bill.TotalAmount > MaximumMoney)
                {
                    throw new BillingException(409, "Số tiền mặt dự kiến sau giao dịch vượt quá giới hạn hỗ trợ. Chưa ghi nhận thanh toán.");
                }

                var outstanding = payOsAttempts.Where(PayOsPaymentRules.IsBlocking).ToArray();
                if (outstanding.Length > 1)
                {
                    throw new BillingException(409, "Chờ đối soát các giao dịch PayOS của hóa đơn.", "PendingReconciliation");
                }

                if (outstanding.Length == 1)
                {
                    if (outstanding[0].Status == PayOsStates.ReviewRequired)
                    {
                        throw new BillingException(409, "Chờ đối soát PayOS. Chưa ghi nhận tiền mặt.", "PendingReconciliation");
                    }

                    throw new OpenPayOsException(outstanding[0].PaymentId);
                }

                if ((!handoverPaymentId.HasValue || payOsAttempts.Length > 0)
                    && !payOsAttempts.Any(value => value.Amount == bill.TotalAmount && PayOsPaymentRules.HasIssuedQr(value)
                        && PayOsPaymentRules.IsVerifiedZero(value)))
                {
                    throw new BillingException(409, "Cần tạo QR PayOS cho hóa đơn trước khi thu tiền mặt.", "QrRequired");
                }

                var paidAt = CashTimestamp(clock.GetUtcNow());
                if (handover is not null && handover.CollectedAt > paidAt)
                {
                    throw new BillingException(409, "Thời điểm xác nhận không hợp lệ so với thời điểm phục vụ thu hộ.");
                }

                if (payment is null)
                {
                    payment = new Payment
                    {
                        PaymentId = Guid.NewGuid(),
                        BillId = billId,
                        Bill = bill,
                        CashShiftId = shift.CashShiftId,
                        MethodCode = settings.Value.MethodCode,
                        Amount = bill.TotalAmount,
                        CashReceived = received,
                        ChangeAmount = received - bill.TotalAmount,
                        IdempotencyKey = key
                    };
                    await payments.AddAsync(payment);
                }

                payment.CashShift = shift;
                payment.Status = PaymentStatuses.Paid;
                payment.PaidAt = paidAt;
                payment.ConfirmedByRef = verified.UserId;
                payment.VerificationSource = PaymentStatuses.CashierVerification;
                if (handover is not null)
                {
                    handover.Status = CashHandoverStatuses.Confirmed;
                    handover.CashierUserRef = verified.UserId;
                    handover.ConfirmedAt = paidAt;
                    handover.ConfirmedAmount = received;
                }
                await movements.AddAsync(new CashMovement
                {
                    CashMovementId = Guid.NewGuid(),
                    CashShiftId = shift.CashShiftId,
                    PaymentId = payment.PaymentId,
                    Payment = payment,
                    Type = PaymentStatuses.SaleMovement,
                    SignedAmount = payment.Amount,
                    Reason = $"Thanh toán tiền mặt cho hóa đơn {bill.BillNumber}.",
                    PostedAt = paidAt,
                    IdempotencyKey = $"Sale:{payment.PaymentId:N}"
                });
                bill.Status = PaymentStatuses.Paid;
                bill.PaidAt = paidAt;
                checkout.Status = PaymentStatuses.Paid;
                if (voucherRedemptions is not null)
                {
                    var redemptions = await voucherRedemptions.GetByBillIdAsync(bill.BillId, ct);
                    foreach (var r in redemptions.Where(x => x.Status == "Reserved"))
                    {
                        r.Status = "Committed";
                        r.CommittedAt = paidAt;
                        await voucherRedemptions.UpdateAsync(r, ct);
                    }
                }
                if (loyaltyClient is not null && bill.CustomerRef.HasValue && bill.CustomerRef.Value != Guid.Empty)
                {
                    await loyaltyClient.EarnPointsAsync(bill.CustomerRef.Value, bill.BillId, bill.TotalAmount, ct);
                    await loyaltyClient.CommitPointsAsync(bill.BillId, ct);
                }
                return (Map(payment, checkout.CheckoutId), true);
            }, ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new BillingException(409, "Dữ liệu thanh toán đã thay đổi. Vui lòng tải lại và thử lại cùng Idempotency-Key.");
        }
        catch (DbUpdateException error) when (error.InnerException is SqlException sql && sql.Number is 1205 or 2601 or 2627)
        {
            throw new BillingException(409, "Hệ thống thanh toán đang bận. Vui lòng thử lại cùng Idempotency-Key.");
        }
        catch (SqlException error) when (error.Number is 1205 or 1222)
        {
            throw new BillingException(409, "Thanh toán đang bận. Vui lòng thử lại cùng Idempotency-Key.");
        }
    }

    public async Task<PaymentDTO> GetById(Guid id, CancellationToken ct)
    {
        var actor = await access.GetCurrentScopeAsync(null, ct);
        RequireRole(actor, false);
        if (id == Guid.Empty)
        {
            throw new BillingException(400, "Mã thanh toán không hợp lệ hoặc bị trống.");
        }

        var payment = await payments.GetDetailsAsync(id, ct) ?? throw new BillingException(404, "Không tìm thấy thanh toán.");
        RequireBranch(actor, payment.Bill.RestaurantId);
        var bill = await bills.GetDetailsAsync(payment.BillId, ct)
            ?? throw new BillingException(409, "Không thể xác minh hóa đơn thanh toán.");
        var sessionIds = bill.BillLines.Select(line => line.TableSessionRef).Distinct().ToArray();
        if (sessionIds.Length != 1)
        {
            throw new BillingException(409, "Thanh toán này không xác định được phiên bàn hợp lệ.");
        }

        var checkout = await checkouts.GetActiveAsync(sessionIds[0], ct);
        if (checkout?.BillId != payment.BillId || checkout.Status != PaymentStatuses.Paid)
        {
            throw new BillingException(409, "Thanh toán này chưa có biên lai checkout hoàn tất.");
        }

        return Map(payment, checkout.CheckoutId);
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
            throw new BillingException(403, write ? "Chỉ thu ngân được phân công mới có thể thu tiền mặt." : "Yêu cầu quyền xem thông tin thanh toán.");
        }
    }

    private static void RequireBranch(BillingAccessScope actor, Guid restaurantId)
    {
        if (!actor.IsAdministrator && !actor.RestaurantIds.Contains(restaurantId))
        {
            throw new BillingException(403, "Yêu cầu phân công đang hoạt động tại nhà hàng này.");
        }
    }

    private static PaymentDTO Map(Payment payment, Guid checkoutId)
    {
        var sale = payment.CashMovements.ToArray();
        if (payment.Status != PaymentStatuses.Paid || payment.CashReceived is not decimal received
            || payment.ChangeAmount is not decimal change || payment.PaidAt is not DateTime paidAt
            || payment.ConfirmedByRef is not Guid cashier || sale.Length != 1
            || sale[0].Type != PaymentStatuses.SaleMovement || sale[0].SignedAmount != payment.Amount
            || sale[0].CashShiftId != payment.CashShiftId || received - change != payment.Amount)
        {
            throw new BillingException(409, "Biên lai tiền mặt đã lưu chưa đầy đủ hoặc không nhất quán. Vui lòng kiểm tra lại trước khi tiếp tục.");
        }

        return new PaymentDTO
        {
            PaymentId = payment.PaymentId,
            BillId = payment.BillId,
            BillNumber = payment.Bill.BillNumber,
            RestaurantId = payment.Bill.RestaurantId,
            CheckoutId = checkoutId,
            CashShiftId = payment.CashShiftId,
            CashRegisterId = payment.CashShift.CashRegisterId,
            MethodCode = payment.MethodCode,
            Status = payment.Status,
            Currency = payment.Bill.Currency,
            Amount = payment.Amount,
            CashReceived = received,
            ChangeAmount = change,
            ConfirmedByRef = cashier,
            PaidAt = new DateTimeOffset(DateTime.SpecifyKind(paidAt, DateTimeKind.Utc)),
            CashMovementId = sale[0].CashMovementId
        };
    }

    internal static bool IsConfirmedHandover(Payment payment)
    {
        return payment.CashHandover is { } handover && handover.Status == CashHandoverStatuses.Confirmed
            && handover.WaiterUserRef != Guid.Empty && handover.PaymentId == payment.PaymentId
            && handover.CashierUserRef.HasValue && handover.CashierUserRef == payment.ConfirmedByRef
            && handover.ConfirmedAt.HasValue && handover.ConfirmedAt == payment.PaidAt
            && handover.CollectedAt <= handover.ConfirmedAt && handover.ConfirmedAmount == payment.CashReceived;
    }

    private sealed class OpenPayOsException(Guid paymentId) : Exception
    {
        public Guid PaymentId { get; } = paymentId;
    }

    internal static DateTime CashTimestamp(DateTimeOffset now)
    {
        return DateTime.SpecifyKind(new SqlDateTime(now.UtcDateTime).Value, DateTimeKind.Utc);
    }
}
