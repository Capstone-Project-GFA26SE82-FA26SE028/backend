using Billing.Common.Constants;
using Billing.Common.DTOs;
using Billing.DAL.Models;

namespace Billing.BLL.Implements;

internal static class ShiftReconciliationCalculator
{
    public static ShiftReconciliationDTO Calculate(CashShift shift, IReadOnlyList<Payment> payments,
        IReadOnlyList<CashMovement> movements, IReadOnlyDictionary<Guid, int> billPaymentCounts, string cashMethod, bool singleActiveShift)
    {
        var result = new ShiftReconciliationDTO
        {
            CashShiftId = shift.CashShiftId,
            RestaurantId = shift.CashRegister.RestaurantId,
            Status = shift.Status,
            OpeningCash = shift.OpeningCash,
            MovementTotal = movements.Sum(value => value.SignedAmount),
            SaleTotal = movements.Where(value => value.Type == PaymentStatuses.SaleMovement).Sum(value => value.SignedAmount),
            CashInTotal = movements.Where(value => value.Type == CashMovementTypes.CashIn).Sum(value => value.SignedAmount),
            CashOutTotal = -movements.Where(value => value.Type == CashMovementTypes.CashOut).Sum(value => value.SignedAmount),
            BankTransferTotal = payments.Where(PayOsPaymentRules.IsVerifiedPaid).Sum(value => value.Amount),
            PaymentCount = payments.Count
        };
        result.ExpectedCash = checked(result.OpeningCash + result.MovementTotal);
        if (shift.Status != CashShiftStatuses.Active || shift.ClosedAt.HasValue)
        {
            Add(result, "ShiftNotActive", "The shift is not active.");
        }
        else if (!singleActiveShift)
        {
            Add(result, "InconsistentActiveShift", "The register does not have exactly this one active shift.");
        }

        if (shift.OpeningCash < 0 || result.ExpectedCash < 0 || result.ExpectedCash > 9999999999999999.99m
            || decimal.Round(result.ExpectedCash, 2) != result.ExpectedCash)
        {
            Add(result, "InvalidExpectedCash", "Expected cash is negative or outside the report amount range.");
        }

        foreach (var payment in payments)
        {
            if (PayOsPaymentRules.IsVerifiedZero(payment))
            {
                continue;
            }

            if (!billPaymentCounts.TryGetValue(payment.BillId, out var paymentCount) || paymentCount != 1)
            {
                Add(result, "DuplicateBillPayments", "This bill has multiple payment records, outside the supported single-payment workflow.", payment.PaymentId);
            }

            if (PayOsPaymentRules.IsPayOs(payment))
            {
                if (!PayOsPaymentRules.IsVerifiedPaid(payment) || payment.Bill.RestaurantId != shift.CashRegister.RestaurantId)
                {
                    Add(result, "UnresolvedPayOsPayment", "PayOS payment is not a verified full settlement or a confirmed zero-money terminal attempt.", payment.PaymentId);
                }

                continue;
            }

            if (payment.Status != PaymentStatuses.Paid)
            {
                Add(result, "UnresolvedPayment", "Payment is not Paid; resolve its outcome before closing.", payment.PaymentId);
            }

            if (payment.MethodCode != cashMethod || payment.VerificationSource != PaymentStatuses.CashierVerification
                || payment.ProviderOrderCode is not null || payment.ProviderTransactionId is not null || payment.Refunds.Count != 0)
            {
                Add(result, "UnsupportedPaymentFlow", "This payment is outside the supported Cashier cash-only workflow.", payment.PaymentId);
            }

            if (payment.CashHandover is not null)
            {
                Add(result, "UnsupportedCashHandover", "Waiter cash handover must be reviewed in its own supported workflow before closing.", payment.PaymentId);
            }

            if (payment.Amount <= 0 || decimal.Truncate(payment.Amount) != payment.Amount
                || payment.CashReceived is not decimal received || payment.ChangeAmount is not decimal change
                || received < payment.Amount || change < 0 || decimal.Truncate(received) != received
                || decimal.Truncate(change) != change || received - change != payment.Amount
                || payment.PaidAt is null || payment.ConfirmedByRef != shift.CashierUserRef || shift.CashierUserRef == Guid.Empty
                || payment.Bill.Status != PaymentStatuses.Paid || payment.Bill.PaidAt != payment.PaidAt
                || payment.Bill.TotalAmount != payment.Amount || payment.Bill.Currency != "VND"
                || payment.Bill.RestaurantId != shift.CashRegister.RestaurantId)
            {
                Add(result, "InconsistentCashPayment", "The payment, tender/change, cashier or paid bill does not reconcile.", payment.PaymentId);
            }

            var linked = payment.CashMovements.ToArray();
            if (linked.Length != 1 || linked[0].CashShiftId != shift.CashShiftId
                || linked[0].Type != PaymentStatuses.SaleMovement || linked[0].SignedAmount != payment.Amount
                || linked[0].PostedAt != payment.PaidAt)
            {
                Add(result, "InconsistentSale", "Payment must have exactly one matching Sale in this shift.", payment.PaymentId);
            }
        }

        foreach (var group in movements.GroupBy(value => value.IdempotencyKey, StringComparer.OrdinalIgnoreCase).Where(group => group.Count() > 1))
        {
            foreach (var movement in group)
            {
                Add(result, "DuplicateMovementKey", "Multiple ledger entries share one request key; review them before closing.", movement.PaymentId, movement.CashMovementId);
            }
        }

        var paymentIds = payments.Select(value => value.PaymentId).ToHashSet();
        foreach (var movement in movements)
        {
            if (movement.Type == PaymentStatuses.SaleMovement)
            {
                if (!movement.PaymentId.HasValue || !paymentIds.Contains(movement.PaymentId.Value) || movement.SignedAmount <= 0)
                {
                    Add(result, "UnmatchedSale", "Sale is not linked to a payment belonging to this shift.", movement.PaymentId, movement.CashMovementId);
                }
            }
            else if (movement.Type is CashMovementTypes.CashIn or CashMovementTypes.CashOut)
            {
                if (movement.PaymentId.HasValue || string.IsNullOrWhiteSpace(movement.Reason)
                    || (movement.Type == CashMovementTypes.CashIn ? movement.SignedAmount <= 0 : movement.SignedAmount >= 0))
                {
                    Add(result, "InconsistentManualMovement", "Outside-bill movement has an invalid sign, reason or payment link.", movement.PaymentId, movement.CashMovementId);
                }
            }
            else
            {
                Add(result, "UnsupportedMovement", "Review the unsupported ledger entry before closing.", movement.PaymentId, movement.CashMovementId);
            }
        }

        result.CanClose = result.Blockers.Count == 0;
        return result;
    }

    private static void Add(ShiftReconciliationDTO result, string code, string message, Guid? paymentId = null, Guid? movementId = null)
    {
        result.Blockers.Add(new ReconciliationBlockerDTO
        {
            Code = code,
            Message = message,
            PaymentId = paymentId,
            CashMovementId = movementId
        });
    }
}
