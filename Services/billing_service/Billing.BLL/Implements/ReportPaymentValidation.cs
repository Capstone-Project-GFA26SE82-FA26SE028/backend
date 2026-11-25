using Billing.Common.Constants;
using Billing.DAL.Models;

namespace Billing.BLL.Implements;

internal static class ReportPaymentValidation
{
    public static bool DineIn(Payment payment, string cashMethod)
    {
        var bill = payment.Bill;
        if (payment.Status != "Paid" || payment.PaidAt is null || bill.Status != "Paid"
            || bill.PaidAt != payment.PaidAt || bill.Currency != "VND" || payment.Amount <= 0
            || decimal.Truncate(payment.Amount) != payment.Amount || payment.Amount != bill.TotalAmount
            || bill.Payments.Count(value => value.Status == "Paid") != 1)
        {
            return false;
        }

        if (payment.MethodCode == cashMethod)
        {
            var handover = payment.CashHandover;
            var sale = payment.CashMovements;
            return payment.VerificationSource == PaymentStatuses.CashierVerification
                && payment.ConfirmedByRef is Guid cashier && cashier != Guid.Empty
                && payment.CashReceived is decimal received && payment.ChangeAmount is decimal change
                && received >= payment.Amount && change >= 0 && received - change == payment.Amount
                && handover is not null && handover.Status == "Confirmed" && handover.WaiterUserRef != Guid.Empty
                && handover.CashierUserRef == cashier && handover.ConfirmedAt == payment.PaidAt
                && handover.ConfirmedAmount == received
                && sale.Count == 1 && sale.Single().Type == PaymentStatuses.SaleMovement
                && sale.Single().SignedAmount == payment.Amount && sale.Single().CashShiftId == payment.CashShiftId;
        }

        return PayOsPaymentRules.IsPayOs(payment) && payment.VerificationSource == PayOsStates.QueryVerified
            && !string.IsNullOrWhiteSpace(payment.ProviderTransactionId)
            && !string.IsNullOrWhiteSpace(payment.ProviderPaymentLinkId)
            && payment.ConfirmedByRef is null && payment.CashReceived is null && payment.ChangeAmount is null
            && payment.CashHandover is null && payment.CashMovements.Count == 0;
    }

    public static bool OnlineSale(OnlinePayment payment)
    {
        return payment.OrderId != Guid.Empty && payment.CustomerUserId != Guid.Empty
            && (payment.Status is "Paid" or "RefundPending" or "Refunded")
            && payment.PaidAt.HasValue && payment.Amount > 0 && decimal.Truncate(payment.Amount) == payment.Amount
            && payment.ProviderOrderCode > 0 && !string.IsNullOrWhiteSpace(payment.LinkId)
            && !string.IsNullOrWhiteSpace(payment.TransactionReference);
    }

    public static bool OnlineRefund(OnlinePayment payment)
    {
        return OnlineSale(payment) && payment.Status == "Refunded" && payment.RefundedAt >= payment.PaidAt
            && payment.RefundVerifiedBy is Guid actor && actor != Guid.Empty
            && !string.IsNullOrWhiteSpace(payment.RefundReference)
            && Uri.TryCreate(payment.RefundEvidence, UriKind.Absolute, out var evidence)
            && evidence.Scheme == Uri.UriSchemeHttps;
    }
}
