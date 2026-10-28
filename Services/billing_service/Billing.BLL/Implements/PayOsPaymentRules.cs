using System.Linq.Expressions;
using Billing.Common.Constants;
using Billing.DAL.Models;

namespace Billing.BLL.Implements;

internal static class PayOsPaymentRules
{
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
