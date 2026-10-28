using System.Data.SqlTypes;
using System.Globalization;
using System.Text.Json;
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

public sealed partial class PayOsPaymentService(IPaymentRepository payments, IBillRepository bills,
    IBillCheckoutRepository checkouts, ICashShiftRepository shifts, ICashRegisterRepository registers,
    IPaymentMethodRepository methods, IPaymentWebhookRepository webhooks, IUnitOfWork unitOfWork,
    IBillingAccess access, IBillService billService, ICheckoutSourceClient ordering, IPayOsClient provider,
    IOptions<CashRegisterAccessSettings> roles, IOptions<PayOsSettings> settings, TimeProvider clock) : IPayOsPaymentService
{
    public async Task<(PayOsPaymentDTO Payment, bool Created)> Create(Guid billId, string key, CreatePayOsPaymentDTO request, CancellationToken ct)
    {
        var actor = await access.GetCurrentScopeAsync(null, ct);
        RequireRole(actor, true);
        if (billId == Guid.Empty || request is null || request.CashShiftId == Guid.Empty || request.CheckoutId == Guid.Empty
            || !Guid.TryParse(key, out var requestId) || requestId == Guid.Empty)
        {
            throw new BillingException(400, "Provide bill, checkout, shift IDs and a UUID Idempotency-Key.");
        }

        key = PayOsStates.KeyPrefix + requestId.ToString("N");
        var initial = await bills.GetDetailsAsync(billId, ct) ?? throw new BillingException(404, "Bill not found.");
        RequireBranch(actor, initial.RestaurantId);
        var sessionId = Session(initial);
        var prepared = await Controlled(async () => await unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            if (!await payments.LockIdempotencyKeyAsync(key, ct) || !await bills.LockSessionAsync(sessionId, ct))
            {
                throw new BillingException(409, "Payment preparation is busy. Retry the same key.");
            }

            var info = await shifts.GetDetailsAsync(request.CashShiftId, ct) ?? throw new BillingException(404, "Cash shift not found.");
            var register = await registers.LockByIdAsync(info.CashRegisterId, ct) ?? throw new BillingException(404, "Cash register not found.");
            var shift = await shifts.LockByIdAsync(request.CashShiftId, ct) ?? throw new BillingException(404, "Cash shift not found.");
            var verified = await access.GetCurrentScopeAsync(initial.RestaurantId, ct);
            RequireRole(verified, true);
            RequireBranch(verified, initial.RestaurantId);
            if (verified.UserId != actor.UserId || shift.CashierUserRef != verified.UserId || register.RestaurantId != initial.RestaurantId)
            {
                throw new BillingException(403, "Use your own shift in the bill's assigned restaurant.");
            }

            var matches = await payments.GetByPayOsRequestKeyAsync(key, ct);
            if (matches.Count > 1)
            {
                throw new BillingException(409, "Payment request records are inconsistent and require review.");
            }

            var previous = matches.SingleOrDefault();
            var checkout = await checkouts.GetByIdAsync(request.CheckoutId, ct);
            if (previous is not null)
            {
                if (previous.IdempotencyKey != key + ":" + request.CheckoutId.ToString("N")
                    || previous.BillId != billId || previous.CashShiftId != request.CashShiftId || !PayOsPaymentRules.IsPayOs(previous)
                    || checkout?.BillId != billId || checkout.TableSessionRef != sessionId)
                {
                    throw new BillingException(409, "Idempotency-Key was used for a different payment request.");
                }

                return (previous.PaymentId, false);
            }

            if (!settings.Value.Enabled)
            {
                throw new BillingException(409, "PayOS creation is disabled. Configure and enable PayOs on the server.");
            }

            provider.EnsureCanCreate();
            var unclosed = await shifts.GetUnclosedAsync(register.CashRegisterId, ct);
            var active = await checkouts.GetActiveAsync(sessionId, ct);
            if (shift.Status != CashShiftStatuses.Active || shift.ClosedAt.HasValue || unclosed.Count != 1
                || unclosed[0].CashShiftId != shift.CashShiftId || checkout?.Status != "Ready"
                || checkout.BillId != billId || checkout.TableSessionRef != sessionId || checkout.ReleasedAt.HasValue
                || active?.CheckoutId != checkout.CheckoutId)
            {
                throw new BillingException(409, "A Ready checkout and your single active cash shift are required.");
            }

            var bill = await bills.GetForRefreshAsync(billId, ct) ?? throw new BillingException(404, "Bill not found.");
            if (bill.Status != "Unpaid" || bill.PaidAt.HasValue || bill.Payments.Any(PayOsPaymentRules.IsBlocking)
                || bill.Currency != "VND" || bill.TotalAmount <= 0 || bill.TotalAmount > 9999999999999999.99m
                || decimal.Truncate(bill.TotalAmount) != bill.TotalAmount)
            {
                throw new BillingException(409, "The bill must be unpaid with a positive whole-VND total and no unresolved or settled payment.");
            }

            var method = await methods.LockByCodeAsync(PayOsStates.Method, ct);
            if (method is null || !method.IsEnabled)
            {
                throw new BillingException(409, "BankTransfer is missing or disabled.");
            }

            var current = await billService.GetById(billId, ct);
            if (current.SourceStatus != "Current" || Hash(bill) != checkout.SourceHash)
            {
                throw new BillingException(409, "The bill and frozen Ordering snapshot do not match. No PayOS request was created.");
            }

            await ordering.BindAsync(sessionId, checkout.CheckoutId, billId, checkout.SourceHash, ct);
            DateTime expires;
            try
            {
                expires = Stamp(clock.GetUtcNow().AddMinutes(settings.Value.LinkLifetimeMinutes));
                if (new DateTimeOffset(expires).ToUnixTimeSeconds() > int.MaxValue)
                {
                    throw new ArgumentOutOfRangeException();
                }
            }
            catch (ArgumentOutOfRangeException)
            {
                throw new BillingException(409, "Configure a supported PayOS link validity period.");
            }

            var payment = new Payment
            {
                PaymentId = Guid.NewGuid(),
                BillId = billId,
                CashShiftId = shift.CashShiftId,
                MethodCode = method.MethodCode,
                Status = PayOsStates.Creating,
                Amount = bill.TotalAmount,
                IdempotencyKey = key + ":" + request.CheckoutId.ToString("N"),
                ProviderOrderCode = await payments.AllocateOrderCodeAsync(ct),
                ExpiredAt = expires
            };
            await payments.AddAsync(payment);
            return (payment.PaymentId, true);
        }, ct));
        var stored = await payments.GetDetailsAsync(prepared.Item1, ct) ?? throw new BillingException(404, "Payment not found.");
        if (!prepared.Item2 && (stored.Status is PaymentStatuses.Paid or PayOsStates.ReviewRequired || PayOsPaymentRules.IsVerifiedZero(stored)))
        {
            return (Map(stored), false);
        }

        var result = await Synchronize(prepared.Item1, ct);
        return (result, prepared.Item2);
    }

    public async Task<PayOsPaymentDTO> GetById(Guid id, CancellationToken ct)
    {
        return Map(await Authorized(id, false, ct));
    }

    public async Task<PayOsPaymentDTO> Reconcile(Guid id, CancellationToken ct)
    {
        await Authorized(id, true, ct);
        return await Synchronize(id, ct);
    }

    public async Task<PayOsPaymentDTO> Cancel(Guid id, CancellationToken ct)
    {
        await Authorized(id, true, ct);
        provider.EnsureConfigured();
        payments.ResetProviderTracking();
        await Controlled(async () => await unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            if (!await payments.LockProviderWorkAsync(id, ct))
            {
                throw new BillingException(409, "PayOS is being reconciled. Retry cancellation for the same payment.");
            }

            var initial = await payments.GetDetailsAsync(id, ct) ?? throw new BillingException(404, "Payment not found.");
            var bill = await bills.GetDetailsAsync(initial.BillId, ct) ?? throw new BillingException(404, "Bill not found.");
            await LockExisting(initial, Session(bill), ct);
            await Authorized(id, true, ct);
            var payment = await payments.GetForProviderUpdateAsync(id, ct) ?? throw new BillingException(404, "Payment not found.");
            if (payment.Status == PaymentStatuses.Paid || payment.PaidAt.HasValue || payment.Status == PayOsStates.ReviewRequired)
            {
                throw new BillingException(409, "A settled or review-required payment cannot be cancelled through this workflow.");
            }

            if (!PayOsPaymentRules.IsVerifiedZero(payment))
            {
                payment.Status = PayOsStates.CancelRequested;
            }

            return true;
        }, ct));
        return await Synchronize(id, ct);
    }

    private async Task<Payment> Authorized(Guid id, bool write, CancellationToken ct)
    {
        var actor = await access.GetCurrentScopeAsync(null, ct);
        RequireRole(actor, write);
        if (id == Guid.Empty)
        {
            throw new BillingException(400, "A valid payment ID is required.");
        }

        var payment = await payments.GetDetailsAsync(id, ct) ?? throw new BillingException(404, "Payment not found.");
        RequireBranch(actor, payment.Bill.RestaurantId);
        if (!PayOsPaymentRules.IsPayOs(payment))
        {
            throw new BillingException(404, "PayOS payment not found.");
        }

        if (write && actor.UserId != payment.CashShift.CashierUserRef)
        {
            throw new BillingException(403, "Only the owning Cashier may manage this PayOS payment.");
        }

        return payment;
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
            throw new BillingException(403, write ? "Only a Cashier can manage PayOS payment requests." : "Billing read permission is required.");
        }
    }

    private static void RequireBranch(BillingAccessScope actor, Guid restaurantId)
    {
        if (!actor.IsAdministrator && !actor.RestaurantIds.Contains(restaurantId))
        {
            throw new BillingException(403, "An active restaurant assignment is required.");
        }
    }

    private async Task LockExisting(Payment value, Guid sessionId, CancellationToken ct)
    {
        if (!await bills.LockSessionAsync(sessionId, ct))
        {
            throw new BillingException(409, "Checkout is busy. Retry the same payment.");
        }

        await registers.LockByIdAsync(value.CashShift.CashRegisterId, ct);
        await shifts.LockByIdAsync(value.CashShiftId, ct);
        await bills.GetForRefreshAsync(value.BillId, ct);
    }

    private static Guid Session(Bill value)
    {
        var ids = value.BillLines.Select(line => line.TableSessionRef).Distinct().ToArray();
        if (ids.Length != 1 || ids[0] == Guid.Empty)
        {
            throw new BillingException(409, "Payment requires a verifiable single-session bill.");
        }

        return ids[0];
    }

    private static string Hash(Bill value)
    {
        try
        {
            return CheckoutHash.Compute(value.BillLines.Select(line =>
            {
                var item = JsonSerializer.Deserialize<BillItemSnapshot>(line.ItemSnapshot) ?? throw new JsonException();
                return new CheckoutHashLine(line.OrderItemRef, item.ItemId, line.Quantity, item.BaseUnitPrice,
                    item.Options.Select(option => new CheckoutHashOption(option.OptionId, option.Quantity, option.ExtraPrice)).ToArray());
            }));
        }
        catch (JsonException)
        {
            throw new BillingException(409, "Stored bill snapshot cannot be verified.");
        }
    }

    private static DateTime Stamp(DateTimeOffset value)
    {
        return DateTime.SpecifyKind(new SqlDateTime(value.UtcDateTime).Value, DateTimeKind.Utc);
    }

    private static PayOsPaymentDTO Map(Payment value)
    {
        return new PayOsPaymentDTO
        {
            PaymentId = value.PaymentId,
            BillId = value.BillId,
            CashShiftId = value.CashShiftId,
            RestaurantId = value.Bill.RestaurantId,
            Status = value.Status,
            Amount = value.Amount,
            ProviderOrderCode = value.ProviderOrderCode,
            ProviderPaymentLinkId = value.ProviderPaymentLinkId,
            CheckoutUrl = value.ProviderCheckoutUrl,
            QrCode = value.ProviderQrCode,
            ProviderTransactionId = value.ProviderTransactionId,
            ExpiresAt = value.ExpiredAt.HasValue ? new DateTimeOffset(DateTime.SpecifyKind(value.ExpiredAt.Value, DateTimeKind.Utc)) : null,
            PaidAt = value.PaidAt.HasValue ? new DateTimeOffset(DateTime.SpecifyKind(value.PaidAt.Value, DateTimeKind.Utc)) : null,
            IsVerifiedZero = PayOsPaymentRules.IsVerifiedZero(value),
            RequiresReview = value.Status == PayOsStates.ReviewRequired,
            LinkDetailsAvailable = !string.IsNullOrEmpty(value.ProviderCheckoutUrl) && !string.IsNullOrEmpty(value.ProviderQrCode)
        };
    }

    private static async Task<T> Controlled<T>(Func<Task<T>> action)
    {
        try
        {
            return await action();
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new BillingException(409, "Payment changed. Retry the same request, not a new payment.");
        }
        catch (DbUpdateException error) when (error.InnerException is SqlException sql && sql.Number is 1205 or 2601 or 2627)
        {
            throw new BillingException(409, "Payment is busy. Retry the same request.");
        }
        catch (SqlException error) when (error.Number is 1205 or 1222)
        {
            throw new BillingException(409, "Payment is busy. Retry the same request.");
        }
    }
}
