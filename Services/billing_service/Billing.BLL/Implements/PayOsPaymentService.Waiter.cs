using Billing.Common.Constants;
using Billing.Common.DTOs;
using Billing.Common.Exceptions;
using Billing.DAL.Models;
using Shared.Common.Pagination;

namespace Billing.BLL.Implements;

public sealed partial class PayOsPaymentService
{
    public async Task<PagedResult<WaiterPayOsPaymentDTO>> GetForCheckout(Guid sessionId, Guid checkoutId,
        int page, int pageSize, CancellationToken ct)
    {
        if (!new PaginationRequest
        {
            Page = page,
            PageSize = pageSize
        }.IsValid())
        {
            throw new BillingException(400, "Yêu cầu phân trang hợp lệ.");
        }

        return await ReadForWaiter(sessionId, checkoutId, async checkout =>
        {
            var values = await payments.GetPayOsPageAsync(checkout.BillId!.Value, checkoutId, page, pageSize, ct);
            var blocking = await payments.GetBlockingIdsAsync(checkout.BillId.Value, PayOsPaymentRules.BlockingExpression, ct);
            var now = clock.GetUtcNow();
            var result = values.Items.Select(value => MapForWaiter(value, checkout, blocking, now)).ToArray();
            return new PagedResult<WaiterPayOsPaymentDTO>(result, values.TotalCount, values.Page, values.PageSize);
        }, ct);
    }

    public async Task<WaiterPayOsPaymentDTO> GetForWaiter(Guid sessionId, Guid checkoutId, Guid paymentId, CancellationToken ct)
    {
        if (paymentId == Guid.Empty)
        {
            throw new BillingException(400, "Yêu cầu mã giao dịch hợp lệ.");
        }

        return await ReadForWaiter(sessionId, checkoutId, async checkout =>
        {
            var value = await payments.GetDetailsAsync(paymentId, ct) ?? throw new BillingException(404, "Không tìm thấy giao dịch PayOS.");
            if (value.BillId != checkout.BillId || !PayOsPaymentRules.IsPayOs(value)
                || !value.IdempotencyKey.EndsWith(":" + checkoutId.ToString("N"), StringComparison.Ordinal))
            {
                throw new BillingException(404, "Giao dịch PayOS không thuộc lần chốt hóa đơn này.");
            }

            var blocking = await payments.GetBlockingIdsAsync(value.BillId, PayOsPaymentRules.BlockingExpression, ct);
            return MapForWaiter(value, checkout, blocking, clock.GetUtcNow());
        }, ct);
    }

    private async Task<T> ReadForWaiter<T>(Guid sessionId, Guid checkoutId, Func<BillCheckout, Task<T>> read, CancellationToken ct)
    {
        var actor = await access.GetCurrentScopeAsync(null, ct);
        RequireWaiterRole(actor);
        if (sessionId == Guid.Empty || checkoutId == Guid.Empty)
        {
            throw new BillingException(400, "Yêu cầu mã phiên và checkout hợp lệ.");
        }

        return await Controlled(async () => await unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            if (!await bills.LockSessionAsync(sessionId, ct))
            {
                throw new BillingException(409, "Thanh toán đang được cập nhật. Vui lòng tải lại trạng thái.");
            }

            var checkout = await RequireWaiterCheckout(sessionId, checkoutId, actor, ct);
            return await read(checkout);
        }, ct));
    }

    private void RequireWaiterRole(BillingAccessScope actor)
    {
        if (actor.UserId == Guid.Empty || actor.IsAdministrator
            || !roles.Value.WaiterRoleIds.Contains(actor.RoleId, StringComparer.Ordinal))
        {
            throw new BillingException(403, "Chỉ phục vụ phụ trách được xem QR và kết quả thanh toán theo phiên.");
        }
    }

    private async Task<BillCheckout> RequireWaiterCheckout(Guid sessionId, Guid checkoutId, BillingAccessScope actor, CancellationToken ct)
    {
        var checkout = await checkouts.GetByIdAsync(checkoutId, ct)
            ?? throw new BillingException(404, "Không tìm thấy lần chốt hóa đơn.");
        if (checkout.TableSessionRef != sessionId)
        {
            throw new BillingException(404, "Checkout không thuộc phiên bàn này.");
        }

        if (checkout.ReleasedAt.HasValue || checkout.Status is not ("Ready" or "Paid") || !checkout.BillId.HasValue
            || !checkout.CashRegisterId.HasValue || !checkout.SubmittedAt.HasValue || !checkout.PreparedAt.HasValue
            || !checkout.PreparedByRef.HasValue || !checkout.PresentedAt.HasValue)
        {
            throw new BillingException(409, "Hóa đơn chưa được chuẩn bị, đưa khách hoặc checkout đã bị hủy.");
        }

        var bill = await bills.GetDetailsAsync(checkout.BillId.Value, ct) ?? throw new BillingException(404, "Không tìm thấy hóa đơn.");
        RequireBranch(actor, bill.RestaurantId);
        var verified = await access.GetCurrentScopeAsync(bill.RestaurantId, ct);
        if (verified.UserId != actor.UserId || verified.IsAdministrator
            || !roles.Value.WaiterRoleIds.Contains(verified.RoleId, StringComparer.Ordinal))
        {
            throw new BillingException(403, "Phiên hoặc vai trò phục vụ đã thay đổi.");
        }

        RequireBranch(verified, bill.RestaurantId);
        if (Session(bill) != sessionId || await requests.GetReviewedWaiterAsync(sessionId, checkoutId, ct) != actor.UserId)
        {
            throw new BillingException(403, "Chỉ phục vụ đã tiếp nhận và kiểm tra yêu cầu được lấy QR hoặc xem kết quả.");
        }

        if (Hash(bill) != checkout.SourceHash)
        {
            throw new BillingException(409, "Hóa đơn không khớp tập món đã chốt. Không thể đưa QR cho khách.");
        }

        return checkout;
    }

    private static WaiterPayOsPaymentDTO MapForWaiter(Payment value, BillCheckout checkout,
        IReadOnlyList<Guid> blocking, DateTimeOffset now)
    {
        if (value.CashShift.CashRegisterId != checkout.CashRegisterId
            || value.CashShift.CashRegister.RestaurantId != value.Bill.RestaurantId)
        {
            throw new BillingException(409, "Giao dịch không khớp quầy nhận và nhà hàng của hóa đơn.");
        }

        var consistent = PayOsPaymentRules.IsPayOs(value)
            && value.IdempotencyKey.EndsWith(":" + checkout.CheckoutId.ToString("N"), StringComparison.Ordinal)
            && value.Amount > 0 && value.Amount == value.Bill.TotalAmount && value.Bill.Currency == "VND"
            && decimal.Truncate(value.Amount) == value.Amount;
        var hasOtherPayment = blocking.Any(id => id != value.PaymentId);
        var canPresent = consistent && !hasOtherPayment && checkout.Status == "Ready" && value.Bill.Status == "Unpaid"
            && !value.Bill.PaidAt.HasValue && value.Status == PayOsStates.Pending && value.PaidAt is null
            && value.VerificationSource != PayOsStates.WebhookVerified && value.ProviderTransactionId is null
            && value.CashShift.Status == CashShiftStatuses.Active && !value.CashShift.ClosedAt.HasValue
            && value.ExpiredAt.HasValue && value.ExpiredAt.Value > now.UtcDateTime
            && !string.IsNullOrWhiteSpace(value.ProviderPaymentLinkId)
            && !string.IsNullOrWhiteSpace(value.ProviderCheckoutUrl) && !string.IsNullOrWhiteSpace(value.ProviderQrCode)
            && value.CashReceived is null && value.ChangeAmount is null && value.CashHandover is null
            && value.CashMovements.Count == 0 && value.Refunds.Count == 0;
        var confirmed = consistent && !hasOtherPayment && checkout.Status == PaymentStatuses.Paid
            && PayOsPaymentRules.IsVerifiedPaid(value);
        var unresolvedPaidState = PayOsPaymentRules.IsBlocking(value) && !confirmed
            && (value.Status == PaymentStatuses.Paid || value.Bill.Status == PaymentStatuses.Paid || checkout.Status == PaymentStatuses.Paid);
        return new WaiterPayOsPaymentDTO
        {
            PaymentId = value.PaymentId,
            BillId = value.BillId,
            TableSessionId = checkout.TableSessionRef,
            CheckoutId = checkout.CheckoutId,
            Status = value.Status,
            BillStatus = value.Bill.Status,
            Amount = value.Amount,
            CanPresentQr = canPresent,
            CheckoutUrl = canPresent ? value.ProviderCheckoutUrl : null,
            QrCode = canPresent ? value.ProviderQrCode : null,
            ExpiresAt = Utc(value.ExpiredAt),
            PaidAt = Utc(value.PaidAt),
            PaymentConfirmed = confirmed,
            RequiresReview = !consistent || unresolvedPaidState || value.Status == PayOsStates.ReviewRequired
                || (hasOtherPayment && PayOsPaymentRules.IsBlocking(value)),
            EvaluatedAt = now
        };
    }

    private static DateTimeOffset? Utc(DateTime? value)
    {
        return value.HasValue ? new DateTimeOffset(DateTime.SpecifyKind(value.Value, DateTimeKind.Utc)) : null;
    }
}
