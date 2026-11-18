using System.Linq.Expressions;
using Billing.Common.Constants;
using Billing.DAL.Models;

namespace Billing.BLL.Implements;

internal static class PayOsPaymentRules
{
    public static bool HasPresentedBill(BillCheckout? checkout)
    {
        return checkout is { ReleasedAt: null, BillId: not null, CashRegisterId: not null, SubmittedAt: not null,
            PreparedByRef: not null, PreparedAt: not null, PresentedAt: not null }
            && !string.IsNullOrWhiteSpace(checkout.SourceHash);
    }

    public static bool BelongsToCheckout(Payment payment, Guid checkoutId)
    {
        return IsPayOs(payment) && payment.IdempotencyKey.EndsWith(":" + checkoutId.ToString("N"), StringComparison.Ordinal);
    }

    public static bool CanPresentQr(Payment payment, DateTimeOffset now)
    {
        return payment.Status == PayOsStates.Pending && payment.PaidAt is null
            && payment.VerificationSource != PayOsStates.WebhookVerified && payment.ProviderTransactionId is null
            && payment.CashShift.Status == CashShiftStatuses.Active && !payment.CashShift.ClosedAt.HasValue
            && payment.Bill.Status == "Unpaid" && !payment.Bill.PaidAt.HasValue
            && payment.ExpiredAt > now.UtcDateTime && !string.IsNullOrWhiteSpace(payment.ProviderPaymentLinkId)
            && !string.IsNullOrWhiteSpace(payment.ProviderCheckoutUrl) && !string.IsNullOrWhiteSpace(payment.ProviderQrCode)
            && payment.CashReceived is null && payment.ChangeAmount is null && payment.CashHandover is null
            && payment.CashMovements.Count == 0 && payment.Refunds.Count == 0;
    }

    public static readonly Expression<Func<Payment, bool>> BlockingExpression = value => !(value.MethodCode == PayOsStates.Method
        && value.IdempotencyKey.StartsWith(PayOsStates.KeyPrefix) && value.ProviderOrderCode != null
        && value.ProviderPaymentLinkId != null && value.ProviderPaymentLinkId != ""
        && PayOsStates.ZeroStatuses.Contains(value.Status) && value.VerificationSource == PayOsStates.ZeroVerified
        && value.PaidAt == null && value.ProviderTransactionId == null && value.CashReceived == null
        && value.ChangeAmount == null && !value.CashMovements.Any() && !value.Refunds.Any() && value.CashHandover == null);
    private static readonly Func<Payment, bool> Blocking = BlockingExpression.Compile();

    public static bool IsBlocking(Payment value)
    {
        return Blocking(value);
    }

    public static bool IsVerifiedZero(Payment value)
    {
        return !Blocking(value);
    }

    public static bool IsVerifiedPaid(Payment value)
    {
        return IsPayOs(value) && value.Status == PaymentStatuses.Paid && value.VerificationSource == PayOsStates.QueryVerified
            && value.PaidAt.HasValue && !string.IsNullOrEmpty(value.ProviderPaymentLinkId) && !string.IsNullOrEmpty(value.ProviderTransactionId)
            && value.ConfirmedByRef is null && value.CashReceived is null && value.ChangeAmount is null
            && value.CashMovements.Count == 0 && value.Refunds.Count == 0 && value.CashHandover is null
            && value.Amount > 0 && decimal.Truncate(value.Amount) == value.Amount && value.Bill.Status == PaymentStatuses.Paid
            && value.Bill.PaidAt == value.PaidAt && value.Bill.TotalAmount == value.Amount && value.Bill.Currency == "VND";
    }

    public static bool IsPayOs(Payment value)
    {
        return value.MethodCode == PayOsStates.Method && value.IdempotencyKey.StartsWith(PayOsStates.KeyPrefix, StringComparison.Ordinal)
            && !string.IsNullOrEmpty(value.ProviderOrderCode);
    }
}
