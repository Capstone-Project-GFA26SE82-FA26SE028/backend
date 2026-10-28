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
using Shared.Common.Billing;
using Shared.Persistence.Interfaces;

namespace Billing.BLL.Implements;

public sealed class PaymentService(IPaymentRepository payments, IBillRepository bills,
    IBillCheckoutRepository checkouts, ICashShiftRepository shifts, ICashRegisterRepository registers,
    IPaymentMethodRepository methods, ICashMovementRepository movements, IUnitOfWork unitOfWork,
    IBillingAccess access, IBillService billService, ICheckoutSourceClient ordering,
    IOptions<CashRegisterAccessSettings> roles, IOptions<CashPaymentSettings> settings, TimeProvider clock) : IPaymentService
{
    private const decimal MaximumMoney = 9999999999999999.99m;

    public async Task<(PaymentDTO Payment, bool Created)> PayCash(Guid billId, string key,
        CreateCashPaymentDTO request, CancellationToken ct)
    {
        var actor = await access.GetCurrentScopeAsync(null, ct);
        RequireRole(actor, true);
        if (billId == Guid.Empty || request is null || request.CheckoutId == Guid.Empty
            || request.CashShiftId == Guid.Empty || !Guid.TryParse(key, out var requestId) || requestId == Guid.Empty
            || request.CashReceived is not decimal received || received < 0 || received > MaximumMoney
            || decimal.Truncate(received) != received)
        {
            throw new BillingException(400, "Provide valid bill, checkout and shift IDs, a UUID Idempotency-Key and a whole VND cash amount.");
        }

        key = requestId.ToString("N");
        var initial = await bills.GetDetailsAsync(billId, ct) ?? throw new BillingException(404, "Bill not found.");
        RequireBranch(actor, initial.RestaurantId);
        var sessions = initial.BillLines.Select(line => line.TableSessionRef).Distinct().ToArray();
        if (sessions.Length != 1 || sessions[0] == Guid.Empty)
        {
            throw new BillingException(409, "The bill must identify exactly one table session before payment.");
        }

        try
        {
            return await unitOfWork.ExecuteInTransactionAsync(async () =>
            {
                if (!await payments.LockIdempotencyKeyAsync(key, ct) || !await bills.LockSessionAsync(sessions[0], ct))
                {
                    throw new BillingException(409, "Payment is busy. Retry with the same Idempotency-Key.");
                }

                var verified = await access.GetCurrentScopeAsync(initial.RestaurantId, ct);
                RequireRole(verified, true);
                RequireBranch(verified, initial.RestaurantId);
                if (actor.UserId != verified.UserId)
                {
                    throw new BillingException(401, "The employee session changed. Sign in again.");
                }

                var checkout = await checkouts.GetByIdAsync(request.CheckoutId, ct);
                var active = await checkouts.GetActiveAsync(sessions[0], ct);
                if (checkout is null || checkout.TableSessionRef != sessions[0] || checkout.BillId != billId
                    || checkout.ReleasedAt.HasValue || active?.CheckoutId != checkout.CheckoutId)
                {
                    throw new BillingException(409, "This bill does not have the requested active checkout.");
                }

                var existing = await payments.GetByKeyAsync(key, ct);
                if (existing is not null)
                {
                    if (existing.BillId != billId || existing.CashShiftId != request.CashShiftId
                        || existing.CashReceived != received || existing.ConfirmedByRef != verified.UserId)
                    {
                        throw new BillingException(409, "Idempotency-Key was already used for a different payment request.");
                    }

                    if (checkout.Status != PaymentStatuses.Paid || existing.Bill.Status != PaymentStatuses.Paid)
                    {
                        throw new BillingException(409, "The stored payment is not a completed cash receipt. Review it before retrying.");
                    }

                    return (Map(existing, checkout.CheckoutId), false);
                }

                if (checkout.Status != "Ready" || string.IsNullOrEmpty(checkout.SourceHash))
                {
                    throw new BillingException(409, "Checkout must be Ready before collecting payment.");
                }

                var shiftInfo = await shifts.GetDetailsAsync(request.CashShiftId, ct)
                    ?? throw new BillingException(404, "Cash shift not found.");
                RequireBranch(verified, shiftInfo.CashRegister.RestaurantId);
                if (shiftInfo.CashRegister.RestaurantId != initial.RestaurantId)
                {
                    throw new BillingException(409, "The cash shift and bill must belong to the same restaurant.");
                }

                var register = await registers.LockByIdAsync(shiftInfo.CashRegisterId, ct)
                    ?? throw new BillingException(404, "Cash register not found.");
                var shift = await shifts.LockByIdAsync(request.CashShiftId, ct)
                    ?? throw new BillingException(404, "Cash shift not found.");
                var unclosed = await shifts.GetUnclosedAsync(register.CashRegisterId, ct);
                if (shift.CashierUserRef != verified.UserId)
                {
                    throw new BillingException(403, "Cashiers can collect payment only in their own cash shift.");
                }

                if (shift.ClosedAt.HasValue || shift.Status != CashShiftStatuses.Active
                    || unclosed.Count != 1 || unclosed[0].CashShiftId != shift.CashShiftId)
                {
                    throw new BillingException(409, "The selected cash shift is not the single active shift for this register.");
                }

                var bill = await bills.GetForRefreshAsync(billId, ct) ?? throw new BillingException(404, "Bill not found.");
                if (bill.RestaurantId != register.RestaurantId || bill.Status != "Unpaid" || bill.PaidAt.HasValue
                    || bill.Payments.Any(PayOsPaymentRules.IsBlocking) || bill.Currency != "VND" || bill.TotalAmount <= 0
                    || bill.TotalAmount > MaximumMoney || decimal.Truncate(bill.TotalAmount) != bill.TotalAmount)
                {
                    throw new BillingException(409, "Only an unpaid, positive whole-VND bill without payment records can be paid in cash.");
                }

                if (received < bill.TotalAmount)
                {
                    throw new BillingException(400, "Cash received must cover the full bill total.");
                }

                var method = await methods.LockByCodeAsync(settings.Value.MethodCode, ct);
                if (method is null || !method.IsEnabled)
                {
                    throw new BillingException(409, "The configured cash payment method is missing or disabled.");
                }

                var current = await billService.GetById(billId, ct);
                if (current.SourceStatus != "Current")
                {
                    throw new BillingException(current.SourceStatus == "Unavailable" ? 503 : 409,
                        "The bill source is not current. No payment was recorded; review checkout before collecting cash.");
                }

                var hash = CheckoutHash.Compute(current.Items.Select(line => new CheckoutHashLine(line.OrderItemRef,
                    line.Item.ItemId, line.Quantity, line.Item.BaseUnitPrice, line.Item.Options.Select(option =>
                        new CheckoutHashOption(option.OptionId, option.Quantity, option.ExtraPrice)).ToArray())));
                if (hash != checkout.SourceHash)
                {
                    throw new BillingException(409, "The bill does not match the Ready checkout item set.");
                }

                await ordering.BindAsync(sessions[0], checkout.CheckoutId, billId, hash, ct);
                var paidAt = DateTime.SpecifyKind(new SqlDateTime(clock.GetUtcNow().UtcDateTime).Value, DateTimeKind.Utc);
                var payment = new Payment
                {
                    PaymentId = Guid.NewGuid(),
                    BillId = billId,
                    Bill = bill,
                    CashShiftId = shift.CashShiftId,
                    CashShift = shift,
                    MethodCode = method.MethodCode,
                    MethodCodeNavigation = method,
                    Status = PaymentStatuses.Paid,
                    Amount = bill.TotalAmount,
                    CashReceived = received,
                    ChangeAmount = received - bill.TotalAmount,
                    IdempotencyKey = key,
                    PaidAt = paidAt,
                    ConfirmedByRef = verified.UserId,
                    VerificationSource = PaymentStatuses.CashierVerification
                };
                if (await movements.PaymentHasMovementAsync(payment.PaymentId, ct))
                {
                    throw new BillingException(409, "This payment already has a cash movement.");
                }

                await payments.AddAsync(payment);
                await movements.AddAsync(new CashMovement
                {
                    CashMovementId = Guid.NewGuid(),
                    CashShiftId = shift.CashShiftId,
                    PaymentId = payment.PaymentId,
                    Payment = payment,
                    Type = PaymentStatuses.SaleMovement,
                    SignedAmount = payment.Amount,
                    Reason = $"Cash payment for bill {bill.BillNumber}.",
                    PostedAt = paidAt,
                    IdempotencyKey = $"Sale:{payment.PaymentId:N}"
                });
                bill.Status = PaymentStatuses.Paid;
                bill.PaidAt = paidAt;
                checkout.Status = PaymentStatuses.Paid;
                return (Map(payment, checkout.CheckoutId), true);
            }, ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new BillingException(409, "Payment data changed. Reload and retry with the same Idempotency-Key.");
        }
        catch (DbUpdateException error) when (error.InnerException is SqlException sql && sql.Number is 1205 or 2601 or 2627)
        {
            throw new BillingException(409, "Payment data is busy. Retry with the same Idempotency-Key.");
        }
        catch (SqlException error) when (error.Number is 1205 or 1222)
        {
            throw new BillingException(409, "Payment is busy. Retry with the same Idempotency-Key.");
        }
    }

    public async Task<PaymentDTO> GetById(Guid id, CancellationToken ct)
    {
        var actor = await access.GetCurrentScopeAsync(null, ct);
        RequireRole(actor, false);
        if (id == Guid.Empty)
        {
            throw new BillingException(400, "A valid payment ID is required.");
        }

        var payment = await payments.GetDetailsAsync(id, ct) ?? throw new BillingException(404, "Payment not found.");
        RequireBranch(actor, payment.Bill.RestaurantId);
        var bill = await bills.GetDetailsAsync(payment.BillId, ct)
            ?? throw new BillingException(409, "The payment bill could not be verified.");
        var sessionIds = bill.BillLines.Select(line => line.TableSessionRef).Distinct().ToArray();
        if (sessionIds.Length != 1)
        {
            throw new BillingException(409, "This payment does not identify a verifiable table session.");
        }

        var checkout = await checkouts.GetActiveAsync(sessionIds[0], ct);
        if (checkout?.BillId != payment.BillId || checkout.Status != PaymentStatuses.Paid)
        {
            throw new BillingException(409, "This payment does not have a completed checkout receipt.");
        }

        return Map(payment, checkout.CheckoutId);
    }

    private void RequireRole(BillingAccessScope actor, bool write)
    {
        if (actor.UserId == Guid.Empty)
        {
            throw new BillingException(401, "A valid employee session is required.");
        }

        var cashier = roles.Value.CashierRoleIds.Contains(actor.RoleId, StringComparer.Ordinal);
        var manager = roles.Value.ManagerRoleIds.Contains(actor.RoleId, StringComparer.Ordinal);
        if (write ? actor.IsAdministrator || !cashier : !actor.IsAdministrator && !cashier && !manager)
        {
            throw new BillingException(403, write ? "Only an assigned Cashier can collect cash payments." : "Billing read permission is required.");
        }
    }

    private static void RequireBranch(BillingAccessScope actor, Guid restaurantId)
    {
        if (!actor.IsAdministrator && !actor.RestaurantIds.Contains(restaurantId))
        {
            throw new BillingException(403, "An active assignment to this restaurant is required.");
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
            throw new BillingException(409, "The stored cash receipt is incomplete or inconsistent. Review it before proceeding.");
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
}
