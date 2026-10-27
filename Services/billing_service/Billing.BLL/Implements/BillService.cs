using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Billing.BLL.Interfaces;
using Billing.Common.DTOs;
using Billing.Common.Exceptions;
using Billing.Common.Settings;
using Billing.DAL.Interfaces;
using Billing.DAL.Models;
using Microsoft.Extensions.Options;
using Shared.Common.Pagination;
using Shared.Persistence.Interfaces;

namespace Billing.BLL.Implements;

public sealed class BillService(IBillRepository bills, ITaxFeePolicyRepository policies,
    IUnitOfWork unitOfWork, IBillingAccess access, IBillSourceClient source,
    IOptions<CashRegisterAccessSettings> roles, IOptions<BillSettings> settings, TimeProvider clock) : IBillService
{
    private const decimal MaximumMoney = 9999999999999999.99m;

    public async Task<BillDTO> Create(Guid tableSessionId, CancellationToken ct)
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
                var unitPrice = Money(line.Item.BaseUnitPrice + line.Item.Options.Sum(option => option.ExtraPrice * option.Quantity));
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

            result.ServiceAmount = Money(decimal.Round(result.SubTotal * result.ServiceRateSnapshot, 2, MidpointRounding.AwayFromZero));
            result.VatAmount = Money(decimal.Round((result.SubTotal + result.ServiceAmount) * result.VatRateSnapshot, 2, MidpointRounding.AwayFromZero));
            result.TotalAmount = Money(decimal.Round(result.SubTotal + result.ServiceAmount + result.VatAmount, 0, MidpointRounding.AwayFromZero));
            Allocate(result.BillLines, result.SubTotal, result.ServiceAmount, (line, value) => line.ServiceAllocated = value);
            Allocate(result.BillLines, result.SubTotal, result.VatAmount, (line, value) => line.VatAllocated = value);
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
        result.SourceStatus = current.RestaurantId == bill.RestaurantId
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
            throw new BillingException(403, create ? "Only a Cashier can create bills." : "Billing read permission is required.");
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
        var canonical = new
        {
            snapshot.TableSessionId,
            snapshot.RestaurantId,
            snapshot.PendingOrderCount,
            Items = snapshot.Items.OrderBy(line => line.OrderItemId).Select(line => new
            {
                line.OrderItemId,
                line.OriginalTableId,
                line.ComboGroupId,
                line.Quantity,
                line.Item.OrderId,
                line.Item.ItemId,
                line.Item.Name,
                line.Item.Status,
                line.Item.BaseUnitPrice,
                Options = line.Item.Options.OrderBy(option => option.OptionId).ToArray()
            })
        };
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(canonical))));
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

        if (result.Items.Any(line => line.Item.Status != "Served"))
        {
            result.Warnings.Add("Some items were not served when this bill was created.");
        }

        return result;
    }
}
