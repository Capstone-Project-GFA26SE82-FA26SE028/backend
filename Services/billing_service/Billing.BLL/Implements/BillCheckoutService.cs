using Billing.BLL.Interfaces;
using Billing.Common.DTOs;
using Billing.Common.Exceptions;
using Billing.Common.Settings;
using Billing.DAL.Interfaces;
using Billing.DAL.Models;
using Microsoft.Extensions.Options;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Shared.Common.Billing;
using Shared.Persistence.Interfaces;

namespace Billing.BLL.Implements;

public sealed class BillCheckoutService(IBillCheckoutRepository checkouts, IBillRepository bills,
    IUnitOfWork unitOfWork, ICheckoutSourceClient ordering, IBillSourceClient source,
    IBillService billService, IBillingAccess access, IOptions<CashRegisterAccessSettings> roles,
    TimeProvider clock) : IBillCheckoutService
{
    public async Task<BillCheckoutDTO> Request(Guid sessionId, Guid id, string guestToken, CancellationToken ct)
    {
        ValidateIds(sessionId, id);
        await ordering.VerifyAsync(sessionId, guestToken, ct);
        if (string.IsNullOrEmpty(guestToken))
        {
            await RequireCashier(sessionId, ct);
        }

        var value = await InSession(sessionId, async () =>
        {
            var existing = await checkouts.GetByIdAsync(id, ct);
            if (existing is not null)
            {
                CheckSession(existing, sessionId);
                return existing;
            }

            if (await checkouts.GetActiveAsync(sessionId, ct) is not null)
            {
                throw new BillingException(409, "This session already has a checkout. Retrieve and continue the current checkout.");
            }

            if (await checkouts.HasPaymentOrFinalBillAsync(sessionId, ct))
            {
                throw new BillingException(409, "This session has payment records or a finalized bill. Additional checkout cannot be requested.");
            }

            var created = new BillCheckout
            {
                CheckoutId = id,
                TableSessionRef = sessionId,
                CreatedAt = clock.GetUtcNow().UtcDateTime,
                Status = "Preparing"
            };
            checkouts.Add(created);
            return created;
        }, ct);
        if (value.ReleasedAt.HasValue || value.Status is "ReleasePending" or "Ready")
        {
            return Map(value);
        }

        var remote = await ordering.BeginAsync(sessionId, id, guestToken, ct);
        return await InSession(sessionId, async () =>
        {
            var current = await Find(sessionId, id, ct);
            if (current.ReleasedAt.HasValue || current.Status is "ReleasePending" or "Ready")
            {
                return Map(current);
            }

            if (remote.Status == "Released")
            {
                throw new BillingException(409, "Ordering has released this checkout. Reconcile the cancellation before proceeding.");
            }

            current.Status = remote.Status;
            current.SourceHash = remote.SourceHash;
            return Map(current);
        }, ct);
    }

    public async Task<BillCheckoutDTO> GetCurrent(Guid sessionId, string guestToken, CancellationToken ct)
    {
        ValidateSessionId(sessionId);
        await ordering.VerifyAsync(sessionId, guestToken, ct);
        var value = await checkouts.GetActiveAsync(sessionId, ct)
            ?? throw new BillingException(404, "No active checkout exists for this session.");
        return Map(value);
    }

    public async Task<BillCheckoutDTO> Synchronize(Guid sessionId, Guid id, CancellationToken ct)
    {
        ValidateIds(sessionId, id);
        await RequireCashier(sessionId, ct);
        var state = await Request(sessionId, id, string.Empty, ct);
        if (state.Status == "WaitingConfirmation")
        {
            return state;
        }

        if (state.Status is not ("Frozen" or "Ready") || state.SourceHash is null)
        {
            throw new BillingException(409, "Checkout is not frozen and cannot be prepared for payment.");
        }

        var billId = await checkouts.GetBillIdAsync(sessionId, ct);
        BillDTO bill;
        if (billId.HasValue)
        {
            bill = await billService.RefreshForCheckout(billId.Value, id, ct);
        }
        else
        {
            try
            {
                bill = await billService.CreateForCheckout(sessionId, id, ct);
            }
            catch (BillingException error) when (error.StatusCode == 409)
            {
                billId = await checkouts.GetBillIdAsync(sessionId, ct);
                if (!billId.HasValue)
                {
                    throw;
                }

                bill = await billService.RefreshForCheckout(billId.Value, id, ct);
            }
        }

        var hash = CheckoutHash.Compute(bill.Items.Select(line => new CheckoutHashLine(line.OrderItemRef,
            line.Item.ItemId, line.Quantity, line.Item.BaseUnitPrice, line.Item.Options.Select(option =>
                new CheckoutHashOption(option.OptionId, option.Quantity, option.ExtraPrice)).ToArray())));
        if (hash != state.SourceHash)
        {
            throw new BillingException(409, "The bill does not match the frozen Ordering item set. Payment is not ready.");
        }

        await ordering.BindAsync(sessionId, id, bill.BillId, hash, ct);
        return await InSession(sessionId, async () =>
        {
            var current = await Find(sessionId, id, ct);
            if (current.Status is not ("Frozen" or "Ready") || current.ReleasedAt.HasValue)
            {
                throw new BillingException(409, "Checkout changed while synchronizing the bill.");
            }

            current.BillId = bill.BillId;
            current.SourceHash = hash;
            current.Status = "Ready";
            return Map(current);
        }, ct);
    }

    public async Task<BillCheckoutDTO> Cancel(Guid sessionId, Guid id, CancellationToken ct)
    {
        ValidateIds(sessionId, id);
        await RequireCashier(sessionId, ct);
        var value = await InSession(sessionId, async () =>
        {
            var current = await Find(sessionId, id, ct);
            if (await checkouts.HasPaymentOrFinalBillAsync(sessionId, ct))
            {
                throw new BillingException(409, "Checkout cannot be cancelled after payment has started or the bill has been finalized.");
            }

            if (!current.ReleasedAt.HasValue)
            {
                current.Status = "ReleasePending";
            }

            return current;
        }, ct);
        if (value.ReleasedAt.HasValue)
        {
            return Map(value);
        }

        await ordering.ReleaseAsync(sessionId, id, ct);
        return await InSession(sessionId, async () =>
        {
            var current = await Find(sessionId, id, ct);
            current.Status = "Released";
            current.ReleasedAt ??= clock.GetUtcNow().UtcDateTime;
            return Map(current);
        }, ct);
    }

    public async Task AuthorizeRelease(Guid sessionId, Guid id, CancellationToken ct)
    {
        ValidateIds(sessionId, id);
        await RequireCashier(sessionId, ct);
        await InSession(sessionId, async () =>
        {
            var value = await Find(sessionId, id, ct);
            if (value.Status is not ("ReleasePending" or "Released")
                || await checkouts.HasPaymentOrFinalBillAsync(sessionId, ct))
            {
                throw new BillingException(409, "Billing has not authorized reopening orders for this checkout.");
            }

            return true;
        }, ct);
    }

    private async Task RequireCashier(Guid sessionId, CancellationToken ct)
    {
        var snapshot = await source.GetBySessionAsync(sessionId, ct);
        var actor = await access.GetCurrentScopeAsync(snapshot.RestaurantId, ct);
        if (actor.UserId == Guid.Empty || actor.IsAdministrator
            || !roles.Value.CashierRoleIds.Contains(actor.RoleId, StringComparer.Ordinal)
            || !actor.RestaurantIds.Contains(snapshot.RestaurantId))
        {
            throw new BillingException(403, "Only a Cashier assigned to this restaurant can manage checkout.");
        }
    }

    private async Task<T> InSession<T>(Guid sessionId, Func<Task<T>> action, CancellationToken ct)
    {
        try
        {
            return await unitOfWork.ExecuteInTransactionAsync(async () =>
            {
                if (!await bills.LockSessionAsync(sessionId, ct))
                {
                    throw new BillingException(409, "Checkout is busy. Retry using the same checkout ID.");
                }

                return await action();
            }, ct);
        }
        catch (DbUpdateException error) when (error.InnerException is SqlException sql && sql.Number is 2601 or 2627 or 1205)
        {
            throw new BillingException(409, "Checkout data changed concurrently. Reload and retry the same checkout ID.");
        }
        catch (SqlException error) when (error.Number is 1205 or 1222)
        {
            throw new BillingException(409, "Checkout is busy. Retry the same checkout ID.");
        }
    }

    private async Task<BillCheckout> Find(Guid sessionId, Guid id, CancellationToken ct)
    {
        var value = await checkouts.GetByIdAsync(id, ct) ?? throw new BillingException(404, "Checkout not found.");
        CheckSession(value, sessionId);
        return value;
    }

    private static void CheckSession(BillCheckout value, Guid sessionId)
    {
        if (value.TableSessionRef != sessionId)
        {
            throw new BillingException(409, "This checkout belongs to another table session.");
        }
    }

    private static BillCheckoutDTO Map(BillCheckout value)
    {
        return new BillCheckoutDTO
        {
            CheckoutId = value.CheckoutId,
            TableSessionId = value.TableSessionRef,
            BillId = value.BillId,
            Status = value.Status,
            SourceHash = value.SourceHash,
            OrderingReopened = value.Status == "Released" && value.ReleasedAt.HasValue
        };
    }

    private static void ValidateIds(Guid sessionId, Guid id)
    {
        ValidateSessionId(sessionId);
        if (id == Guid.Empty)
        {
            throw new BillingException(400, "A valid checkout ID is required.");
        }
    }

    private static void ValidateSessionId(Guid sessionId)
    {
        if (sessionId == Guid.Empty)
        {
            throw new BillingException(400, "A valid table session ID is required.");
        }
    }
}
