using System.Text.Json;
using Billing.BLL.Interfaces;
using Billing.Common.DTOs;
using Billing.Common.Exceptions;
using Billing.Common.Settings;
using Billing.DAL.Interfaces;
using Billing.DAL.Models;
using Microsoft.Extensions.Options;
using Shared.Common.Pagination;
using Shared.Common.Billing;
using Shared.Persistence.Interfaces;

namespace Billing.BLL.Implements;

public sealed class BillService(IBillRepository bills, ITaxFeePolicyRepository policies,
    IUnitOfWork unitOfWork, IBillingAccess access, IBillSourceClient source,
    IOptions<CashRegisterAccessSettings> roles, IOptions<BillSettings> settings, TimeProvider clock) : IBillService
{
    private const decimal MaximumMoney = 9999999999999999.99m;

    public Task<BillDTO> Create(Guid tableSessionId, CancellationToken ct)
    {
        return CreateCore(tableSessionId, null, ct);
    }

    public Task<BillDTO> CreateForCheckout(Guid tableSessionId, Guid checkoutId, CancellationToken ct)
    {
        return CreateCore(tableSessionId, checkoutId, ct);
    }

    private async Task<BillDTO> CreateCore(Guid tableSessionId, Guid? checkoutId, CancellationToken ct)
    {
        var actor = await access.GetCurrentScopeAsync(null, ct);
        RequireRole(actor, true);
        if (tableSessionId == Guid.Empty)
        {
            throw new BillingException(400, "A valid table session ID is required.");
        }

        if (settings.Value.Currency != "VND" || settings.Value.OrderPriceBasis != "Exclusive")
        {
            throw new BillingException(409, "Configure VND and explicitly confirm that order prices exclude VAT and service fees before creating bills.");
        }

        var bill = await unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            if (!await bills.LockSessionAsync(tableSessionId, ct))
            {
                throw new BillingException(409, "Bill creation is busy for this table session. Retry the request.");
            }

            await RequireCheckout(tableSessionId, checkoutId, ct);
            if (await bills.SessionHasBillAsync(tableSessionId, ct))
            {
                throw new BillingException(409, "This session already has a bill. Review its freshness before proceeding.");
            }

            var snapshot = await source.GetBySessionAsync(tableSessionId, ct);
            ValidateSource(snapshot, tableSessionId);
            var verified = await access.GetCurrentScopeAsync(snapshot.RestaurantId, ct);
            RequireRole(verified, true);
            if (actor.UserId != verified.UserId)
            {
                throw new BillingException(401, "The employee session changed. Sign in again.");
            }

            RequireBranch(verified, snapshot.RestaurantId);
            if (snapshot.PendingOrderCount != 0)
            {
                throw new BillingException(409, "The session contains orders awaiting waiter confirmation. Resolve them before creating a bill.");
            }

            var effective = await policies.GetEffectiveAsync(clock.GetUtcNow().UtcDateTime, ct);
            if (effective.Count != 1 || effective[0].VatRate is < 0 or > 1
                || effective[0].ServiceRate is < 0 or > 1)
            {
                throw new BillingException(409, "A single valid effective tax and service fee policy is required. Rates must be decimal fractions between 0 and 1.");
            }

            var policy = effective[0];
            var fingerprint = Fingerprint(snapshot);
            var result = new Bill
            {
                BillId = Guid.NewGuid(),
                RestaurantId = snapshot.RestaurantId,
                Status = "Unpaid",
                Currency = settings.Value.Currency,
                DiscountAmount = 0,
                VatRateSnapshot = policy.VatRate,
                ServiceRateSnapshot = policy.ServiceEnabled ? policy.ServiceRate : 0
            };
            result.BillNumber = $"BILL-{result.BillId:N}";
            foreach (var line in snapshot.Items)
            {
                line.Item.SourceFingerprint = fingerprint;
                var unitPrice = UnitPrice(line.Item);
                var total = Money(unitPrice * line.Quantity);
                result.SubTotal = Money(result.SubTotal + total);
                result.BillLines.Add(new BillLine
                {
                    BillLineId = Guid.NewGuid(),
                    BillId = result.BillId,
                    OrderItemRef = line.OrderItemId,
                    TableSessionRef = tableSessionId,
                    OriginalTableRef = line.OriginalTableId,
                    ComboGroupRef = line.ComboGroupId,
                    ItemSnapshot = JsonSerializer.Serialize(line.Item),
                    Quantity = line.Quantity,
                    UnitPriceSnapshot = unitPrice,
                    CogsSnapshot = null
                });
            }

            CalculateTotals(result, result.ServiceRateSnapshot, result.VatRateSnapshot);
            var latest = await source.GetBySessionAsync(tableSessionId, ct);
            ValidateSource(latest, tableSessionId);
            if (Fingerprint(latest) != fingerprint)
            {
                throw new BillingException(409, "Order data changed while creating the bill. Review the orders and retry.");
            }

            await bills.AddAsync(result);
            return result;
        }, ct);
        var dto = Map(bill);
        dto.SourceStatus = "MatchedAtCreation";
        return dto;
    }

    public Task<BillDTO> Refresh(Guid id, CancellationToken ct)
    {
        return RefreshCore(id, null, ct);
    }

    public Task<BillDTO> RefreshForCheckout(Guid id, Guid checkoutId, CancellationToken ct)
    {
        return RefreshCore(id, checkoutId, ct);
    }

    private async Task<BillDTO> RefreshCore(Guid id, Guid? checkoutId, CancellationToken ct)
    {
        var actor = await access.GetCurrentScopeAsync(null, ct);
        RequireRole(actor, true);
        if (id == Guid.Empty)
        {
            throw new BillingException(400, "A valid bill ID is required.");
        }

        var initial = await bills.GetDetailsAsync(id, ct) ?? throw new BillingException(404, "Bill not found.");
        RequireBranch(actor, initial.RestaurantId);
        var initialSessions = initial.BillLines.Select(line => line.TableSessionRef).Distinct().ToArray();
        if (initialSessions.Length != 1)
        {
            throw new BillingException(409, "The bill must identify one table session.");
        }

        var bill = await unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            if (!await bills.LockSessionAsync(initialSessions[0], ct))
            {
                throw new BillingException(409, "Checkout is busy. Retry the request.");
            }

            await RequireCheckout(initialSessions[0], checkoutId, ct);
            var stored = await bills.GetForRefreshAsync(id, ct)
                ?? throw new BillingException(404, "Bill not found.");
            RequireBranch(actor, stored.RestaurantId);
            var verified = await access.GetCurrentScopeAsync(stored.RestaurantId, ct);
            RequireRole(verified, true);
            if (actor.UserId != verified.UserId)
            {
                throw new BillingException(401, "The employee session changed. Sign in again.");
            }

            RequireBranch(verified, stored.RestaurantId);
            if (stored.Status != "Unpaid" || stored.PaidAt.HasValue || stored.Payments.Any(PayOsPaymentRules.IsBlocking))
            {
                throw new BillingException(409, "Only an unpaid bill without payment records can be refreshed.");
            }

            if (stored.Currency != "VND" || settings.Value.OrderPriceBasis != "Exclusive"
                || stored.DiscountAmount != 0 || stored.BillAdjustments.Count != 0
                || stored.VoucherRedemptions.Count != 0 || stored.BillLines.Any(line => line.DiscountAllocated != 0)
                || stored.VatRateSnapshot is < 0 or > 1 || stored.ServiceRateSnapshot is < 0 or > 1)
            {
                throw new BillingException(409, "This bill cannot be refreshed using the supported exclusive-price, undiscounted VND calculation.");
            }

            var sessions = stored.BillLines.Select(line => line.TableSessionRef).Distinct().ToArray();
            if (sessions.Length != 1 || sessions[0] == Guid.Empty
                || stored.BillLines.Select(line => line.OrderItemRef).Distinct().Count() != stored.BillLines.Count)
            {
                throw new BillingException(409, "The bill must contain a verifiable single-session snapshot without duplicate order items.");
            }

            var sessionId = sessions[0];
            var snapshot = await source.GetBySessionAsync(sessionId, ct);
            ValidateSource(snapshot, sessionId, requireItems: false);
            if (snapshot.RestaurantId != stored.RestaurantId)
            {
                throw new BillingException(409, "The order source does not belong to this bill's restaurant.");
            }

            if (snapshot.PendingOrderCount != 0)
            {
                throw new BillingException(409, "Resolve orders awaiting waiter confirmation before refreshing the bill.");
            }

            var fingerprint = Fingerprint(snapshot);
            var draft = BuildRefresh(stored, snapshot, fingerprint);
            var latest = await source.GetBySessionAsync(sessionId, ct);
            ValidateSource(latest, sessionId, requireItems: false);
            if (Fingerprint(latest) != fingerprint)
            {
                throw new BillingException(409, "Order data changed while refreshing the bill. Review the orders and retry.");
            }

            foreach (var updated in draft.BillLines)
            {
                var existing = stored.BillLines.SingleOrDefault(line => line.OrderItemRef == updated.OrderItemRef);
                if (existing is null)
                {
                    stored.BillLines.Add(updated);
                    continue;
                }

                existing.ItemSnapshot = updated.ItemSnapshot;
                existing.Quantity = updated.Quantity;
                existing.UnitPriceSnapshot = updated.UnitPriceSnapshot;
                existing.OriginalTableRef = updated.OriginalTableRef;
                existing.ComboGroupRef = updated.ComboGroupRef;
                existing.VatAllocated = updated.VatAllocated;
                existing.ServiceAllocated = updated.ServiceAllocated;
            }

            stored.SubTotal = draft.SubTotal;
            stored.ServiceAmount = draft.ServiceAmount;
            stored.VatAmount = draft.VatAmount;
            stored.TotalAmount = draft.TotalAmount;
            return stored;
        }, ct);
        var result = Map(bill);
        result.SourceStatus = "MatchedAtRefresh";
        return result;
    }

    private static Bill BuildRefresh(Bill stored, BillSourceDTO snapshot, string fingerprint)
    {
        var draft = new Bill();
        var current = snapshot.Items.ToDictionary(line => line.OrderItemId);
        foreach (var previous in stored.BillLines)
        {
            BillItemSnapshot item;
            try
            {
                item = JsonSerializer.Deserialize<BillItemSnapshot>(previous.ItemSnapshot)
                    ?? throw new JsonException();
            }
            catch (JsonException)
            {
                throw new BillingException(409, "The stored item snapshot cannot be verified for refresh.");
            }

            if (item.ItemId == Guid.Empty || item.OrderId == Guid.Empty || string.IsNullOrEmpty(item.SourceFingerprint))
            {
                throw new BillingException(409, "The stored item snapshot cannot be verified for refresh.");
            }

            var quantity = 0;
            var price = previous.UnitPriceSnapshot;
            var table = previous.OriginalTableRef;
            var combo = previous.ComboGroupRef;
            if (current.Remove(previous.OrderItemRef, out var line))
            {
                if (item.OrderId != line.Item.OrderId || item.ItemId != line.Item.ItemId)
                {
                    throw new BillingException(409, "An order item changed identity. Review the source before refreshing.");
                }

                item = line.Item;
                quantity = line.Quantity;
                price = UnitPrice(item);
                table = line.OriginalTableId;
                combo = line.ComboGroupId;
            }
            else
            {
                item.Status = "Cancelled";
            }

            item.SourceFingerprint = fingerprint;
            draft.BillLines.Add(new BillLine
            {
                BillLineId = previous.BillLineId,
                BillId = stored.BillId,
                OrderItemRef = previous.OrderItemRef,
                TableSessionRef = snapshot.TableSessionId,
                OriginalTableRef = table,
                ComboGroupRef = combo,
                Quantity = quantity,
                UnitPriceSnapshot = price,
                CogsSnapshot = previous.CogsSnapshot,
                ItemSnapshot = JsonSerializer.Serialize(item)
            });
        }

        foreach (var line in current.Values)
        {
            line.Item.SourceFingerprint = fingerprint;
            draft.BillLines.Add(new BillLine
            {
                BillLineId = Guid.NewGuid(),
                BillId = stored.BillId,
                OrderItemRef = line.OrderItemId,
                TableSessionRef = snapshot.TableSessionId,
                OriginalTableRef = line.OriginalTableId,
                ComboGroupRef = line.ComboGroupId,
                Quantity = line.Quantity,
                UnitPriceSnapshot = UnitPrice(line.Item),
                CogsSnapshot = null,
                ItemSnapshot = JsonSerializer.Serialize(line.Item)
            });
        }

        foreach (var line in draft.BillLines)
        {
            draft.SubTotal = Money(draft.SubTotal + Money(line.Quantity * line.UnitPriceSnapshot));
        }

        CalculateTotals(draft, stored.ServiceRateSnapshot, stored.VatRateSnapshot);
        return draft;
    }

    private static decimal UnitPrice(BillItemSnapshot item)
    {
        return Money(item.BaseUnitPrice + item.Options.Sum(option => option.ExtraPrice * option.Quantity));
    }

    private static void CalculateTotals(Bill bill, decimal serviceRate, decimal vatRate)
    {
        bill.ServiceAmount = Money(decimal.Round(bill.SubTotal * serviceRate, 2, MidpointRounding.AwayFromZero));
        bill.VatAmount = Money(decimal.Round((bill.SubTotal + bill.ServiceAmount) * vatRate, 2, MidpointRounding.AwayFromZero));
        bill.TotalAmount = Money(decimal.Round(bill.SubTotal + bill.ServiceAmount + bill.VatAmount, 0, MidpointRounding.AwayFromZero));
        Allocate(bill.BillLines, bill.SubTotal, bill.ServiceAmount, (line, value) => line.ServiceAllocated = value);
        Allocate(bill.BillLines, bill.SubTotal, bill.VatAmount, (line, value) => line.VatAllocated = value);
    }

    public async Task<PagedResult<BillDTO>> GetAll(BillQueryDTO query, CancellationToken ct)
    {
        var actor = await access.GetCurrentScopeAsync(null, ct);
        RequireRole(actor, false);
        if (query is null || !new PaginationRequest
        {
            Page = query.Page,
            PageSize = query.PageSize
        }.IsValid() || query.RestaurantId == Guid.Empty || query.Search?.Length > 50 || query.Search?.Any(char.IsControl) == true)
        {
            throw new BillingException(400, "Provide valid pagination, an optional restaurant ID and a search of at most 50 characters.");
        }

        if (query.RestaurantId.HasValue)
        {
            RequireBranch(actor, query.RestaurantId.Value);
        }

        var page = await bills.GetPageAsync(query, actor.IsAdministrator ? null : actor.RestaurantIds, ct);
        return new PagedResult<BillDTO>(page.Items.Select(Map).ToArray(), page.TotalCount, page.Page, page.PageSize);
    }

    public async Task<BillDTO> GetById(Guid id, CancellationToken ct)
    {
        var actor = await access.GetCurrentScopeAsync(null, ct);
        RequireRole(actor, false);
        if (id == Guid.Empty)
        {
            throw new BillingException(400, "A valid bill ID is required.");
        }

        var bill = await bills.GetDetailsAsync(id, ct) ?? throw new BillingException(404, "Bill not found.");
        RequireBranch(actor, bill.RestaurantId);
        var result = Map(bill);
        var sessions = bill.BillLines.Select(line => line.TableSessionRef).Distinct().ToArray();
        if (sessions.Length != 1 || result.Items.Any(line => string.IsNullOrEmpty(line.Item.SourceFingerprint)))
        {
            result.SourceStatus = "Unverifiable";
            result.Warnings.Add("The stored bill does not contain a verifiable single-session source snapshot.");
            return result;
        }

        BillSourceDTO current;
        try
        {
            current = await source.GetBySessionAsync(sessions[0], ct);
        }
        catch (BillingException error) when (error.StatusCode is 503 or 504 or 404 or 409)
        {
            result.SourceStatus = "Unavailable";
            result.Warnings.Add("Order freshness could not be verified. Do not collect payment using this snapshot.");
            return result;
        }
        ValidateSource(current, sessions[0], requireItems: false);
        result.SourceStatus = current.PendingOrderCount == 0 && current.RestaurantId == bill.RestaurantId
            && result.Items.All(line => line.Item.SourceFingerprint == Fingerprint(current)) ? "Current" : "Stale";
        if (result.SourceStatus == "Stale")
        {
            result.Warnings.Add("Order data changed after this bill was created. Do not collect payment using this snapshot.");
        }

        return result;
    }

    private void RequireRole(BillingAccessScope actor, bool create)
    {
        if (actor.UserId == Guid.Empty)
        {
            throw new BillingException(401, "A valid employee session is required.");
        }

        var cashier = roles.Value.CashierRoleIds.Contains(actor.RoleId, StringComparer.Ordinal);
        var manager = roles.Value.ManagerRoleIds.Contains(actor.RoleId, StringComparer.Ordinal);
        if (create ? actor.IsAdministrator || !cashier : !actor.IsAdministrator && !cashier && !manager)
        {
            throw new BillingException(403, create ? "Only a Cashier can create or refresh bills." : "Billing read permission is required.");
        }

        if (!actor.IsAdministrator && actor.RestaurantIds.Count == 0)
        {
            throw new BillingException(403, "An active restaurant assignment is required.");
        }
    }

    private static void RequireBranch(BillingAccessScope actor, Guid branch)
    {
        if (!actor.IsAdministrator && !actor.RestaurantIds.Contains(branch))
        {
            throw new BillingException(403, "An active assignment to this restaurant is required.");
        }
    }

    private static void ValidateSource(BillSourceDTO snapshot, Guid sessionId, bool requireItems = true)
    {
        if (snapshot.TableSessionId != sessionId || snapshot.RestaurantId == Guid.Empty
            || snapshot.Items.Select(line => line.OrderItemId).Distinct().Count() != snapshot.Items.Count
            || snapshot.Items.Any(line => line.OrderItemId == Guid.Empty || line.OriginalTableId == Guid.Empty
                || line.Item.ItemId == Guid.Empty || line.Item.OrderId == Guid.Empty || string.IsNullOrWhiteSpace(line.Item.Name)
                || line.Quantity <= 0 || line.Item.BaseUnitPrice < 0 || line.Item.BaseUnitPrice > MaximumMoney
                || decimal.Round(line.Item.BaseUnitPrice, 2) != line.Item.BaseUnitPrice
                || line.Item.Options.Any(option => option.OptionId == Guid.Empty || option.Quantity <= 0
                    || option.ExtraPrice < 0 || option.ExtraPrice > MaximumMoney || decimal.Round(option.ExtraPrice, 2) != option.ExtraPrice)))
        {
            throw new BillingException(503, "Ordering returned inconsistent billing data.");
        }

        if (requireItems && snapshot.Items.Count == 0)
        {
            throw new BillingException(409, "There are no confirmed non-cancelled items to bill.");
        }
    }

    private static decimal Money(decimal value)
    {
        if (value < 0 || value > MaximumMoney)
        {
            throw new BillingException(409, "The bill amount exceeds decimal(18,2) storage limits.");
        }

        return value;
    }

    private static void Allocate(ICollection<BillLine> lines, decimal subtotal, decimal amount, Action<BillLine, decimal> assign)
    {
        var ordered = lines.OrderBy(line => line.OrderItemRef).ToArray();
        decimal cumulative = 0;
        decimal allocated = 0;
        for (var index = 0; index < ordered.Length; index++)
        {
            cumulative += ordered[index].Quantity * ordered[index].UnitPriceSnapshot;
            var target = index == ordered.Length - 1 ? amount
                : subtotal == 0 ? 0 : decimal.Round(amount * (cumulative / subtotal), 2, MidpointRounding.AwayFromZero);
            var value = target - allocated;
            assign(ordered[index], value);
            allocated = target;
        }
    }

    private static string Fingerprint(BillSourceDTO snapshot)
    {
        return CheckoutHash.Compute(snapshot.Items.Select(line => new CheckoutHashLine(line.OrderItemId,
            line.Item.ItemId, line.Quantity, line.Item.BaseUnitPrice, line.Item.Options.Select(option =>
                new CheckoutHashOption(option.OptionId, option.Quantity, option.ExtraPrice)).ToArray())));
    }

    private async Task RequireCheckout(Guid sessionId, Guid? checkoutId, CancellationToken ct)
    {
        var active = await bills.GetActiveCheckoutAsync(sessionId, ct);
        if (active is null && !checkoutId.HasValue)
        {
            return;
        }

        if (active is null || active.CheckoutId != checkoutId || active.Status is not ("Frozen" or "Ready"))
        {
            throw new BillingException(409, "Continue the active checkout to synchronize this bill.");
        }
    }

    private static BillDTO Map(Bill bill)
    {
        var result = new BillDTO
        {
            BillId = bill.BillId,
            RestaurantId = bill.RestaurantId,
            BillNumber = bill.BillNumber,
            Status = bill.Status,
            Currency = bill.Currency,
            SubTotal = bill.SubTotal,
            DiscountAmount = bill.DiscountAmount,
            VatRateSnapshot = bill.VatRateSnapshot,
            ServiceRateSnapshot = bill.ServiceRateSnapshot,
            VatAmount = bill.VatAmount,
            ServiceAmount = bill.ServiceAmount,
            TotalAmount = bill.TotalAmount,
            RoundingAdjustment = bill.TotalAmount - (bill.SubTotal - bill.DiscountAmount + bill.ServiceAmount + bill.VatAmount),
            Version = Convert.ToBase64String(bill.Version ?? []),
            HasCompleteCogs = bill.BillLines.Count > 0 && bill.BillLines.All(line => line.CogsSnapshot.HasValue),
            Items = bill.BillLines.OrderBy(line => line.OrderItemRef).Select(line => new BillLineDTO
            {
                BillLineId = line.BillLineId,
                OrderItemRef = line.OrderItemRef,
                TableSessionRef = line.TableSessionRef,
                OriginalTableRef = line.OriginalTableRef,
                ComboGroupRef = line.ComboGroupRef,
                Item = JsonSerializer.Deserialize<BillItemSnapshot>(line.ItemSnapshot) ?? new BillItemSnapshot(),
                Quantity = line.Quantity,
                UnitPriceSnapshot = line.UnitPriceSnapshot,
                CogsSnapshot = line.CogsSnapshot,
                LineTotal = line.Quantity * line.UnitPriceSnapshot
            }).ToList()
        };
        if (!result.HasCompleteCogs)
        {
            result.Warnings.Add("Ingredient cost data is unavailable. Complete COGS and profit cannot be calculated.");
        }

        if (result.Items.Any(line => line.Quantity > 0 && line.Item.Status != "Served"))
        {
            result.Warnings.Add("Some items were not served when this bill was created.");
        }

        if (result.Items.Count > 0 && result.Items.All(line => line.Quantity == 0))
        {
            result.Warnings.Add("All previously billed items are cancelled. Zero-quantity lines retain the bill's session history.");
        }

        return result;
    }
}
