using Billing.BLL.Interfaces;
using Billing.Common.Constants;
using Billing.Common.Exceptions;
using Billing.Common.Settings;
using Billing.DAL.Interfaces;
using Billing.DAL.Models;
using Microsoft.Extensions.Options;
using Shared.Persistence.Interfaces;

namespace Billing.BLL.Implements;

public sealed class PaymentSettlementService(IBillRepository bills, IBillCheckoutRepository checkouts,
    IPaymentRepository payments, IUnitOfWork unitOfWork, IOptions<CashPaymentSettings> settings) : IPaymentSettlementService
{
    public async Task<Guid?> GetVerifiedCheckout(Guid restaurantId, Guid sessionId, CancellationToken ct)
    {
        return await unitOfWork.ExecuteInTransactionAsync<Guid?>(async () =>
        {
            if (!await bills.LockSessionAsync(sessionId, ct))
            {
                throw new BillingException(409, "Thanh toán đang được cập nhật. Vui lòng thử lại.");
            }

            var checkout = await checkouts.GetActiveAsync(sessionId, ct);
            if (checkout is null || checkout.Status != PaymentStatuses.Paid || !PayOsPaymentRules.HasPresentedBill(checkout))
            {
                return null;
            }

            var bill = await bills.GetForRefreshAsync(checkout.BillId!.Value, ct);
            if (bill is null || bill.RestaurantId != restaurantId || bill.Status != PaymentStatuses.Paid || !bill.PaidAt.HasValue
                || bill.Currency != "VND" || bill.TotalAmount <= 0 || decimal.Truncate(bill.TotalAmount) != bill.TotalAmount
                || bill.BillLines.Count == 0 || bill.BillLines.Any(line => line.TableSessionRef != sessionId)
                || PayOsPaymentService.Hash(bill) != checkout.SourceHash)
            {
                return null;
            }

            var blocking = bill.Payments.Where(PayOsPaymentRules.IsBlocking).ToArray();
            if (blocking.Length != 1)
            {
                return null;
            }

            var payment = await payments.GetDetailsAsync(blocking[0].PaymentId, ct);
            if (payment is null || payment.CashShift.CashRegisterId != checkout.CashRegisterId
                || payment.CashShift.CashRegister.RestaurantId != restaurantId)
            {
                return null;
            }

            var verified = PayOsPaymentRules.BelongsToCheckout(payment, checkout.CheckoutId) && PayOsPaymentRules.IsVerifiedPaid(payment)
                || IsVerifiedCash(payment);
            return verified ? checkout.CheckoutId : null;
        }, ct);
    }

    private bool IsVerifiedCash(Payment payment)
    {
        return payment.MethodCode == settings.Value.MethodCode && payment.Status == PaymentStatuses.Paid
            && payment.VerificationSource == PaymentStatuses.CashierVerification
            && (payment.CashHandover is null || PaymentService.IsConfirmedHandover(payment))
            && payment.ProviderPaymentLinkId is null && payment.ProviderOrderCode is null && payment.ProviderTransactionId is null
            && payment.Refunds.Count == 0 && payment.PaidAt == payment.Bill.PaidAt
            && payment.ConfirmedByRef == payment.CashShift.CashierUserRef && payment.CashShift.CashierUserRef != Guid.Empty
            && payment.Amount == payment.Bill.TotalAmount && payment.CashReceived is decimal received
            && payment.ChangeAmount is decimal change && received >= payment.Amount && change == received - payment.Amount
            && decimal.Truncate(received) == received
            && payment.CashMovements.Count == 1 && payment.CashMovements.Any(sale => sale.Type == PaymentStatuses.SaleMovement
                && sale.SignedAmount == payment.Amount && sale.CashShiftId == payment.CashShiftId && sale.PostedAt == payment.PaidAt);
    }
}
