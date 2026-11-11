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
    IOptions<CashRegisterAccessSettings> roles, IOptions<BillSettings> settings, TimeProvider clock, IPaymentRequestClient paymentRequests) : IBillService
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
            throw new BillingException(400, "Mã phiên bàn không hợp lệ hoặc bị trống.");
        }

        if (settings.Value.Currency != "VND" || settings.Value.OrderPriceBasis != "Exclusive")
        {
            throw new BillingException(409, "Cần cấu hình đơn vị tiền VND và xác nhận giá món chưa bao gồm VAT và phí dịch vụ trước khi tạo hóa đơn.");
        }

        var bill = await unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            if (!await bills.LockSessionAsync(tableSessionId, ct))
            {
                throw new BillingException(409, "Hệ thống tạo hóa đơn đang bận cho phiên bàn này. Vui lòng thử lại.");
            }

            await RequireCheckout(tableSessionId, checkoutId, ct);
            if (await bills.SessionHasBillAsync(tableSessionId, ct))
            {
                throw new BillingException(409, "Phiên này đã có hóa đơn. Vui lòng kiểm tra tính cập nhật trước khi tiếp tục.");
            }

            var snapshot = await source.GetBySessionAsync(tableSessionId, ct);
            ValidateSource(snapshot, tableSessionId);
            var verified = await access.GetCurrentScopeAsync(snapshot.RestaurantId, ct);
            RequireRole(verified, true);
            if (actor.UserId != verified.UserId)
            {
                throw new BillingException(401, "Phiên làm việc của nhân viên đã thay đổi. Vui lòng đăng nhập lại.");
            }

            RequireBranch(verified, snapshot.RestaurantId);
            if (snapshot.PendingOrderCount != 0)
            {
                throw new BillingException(409, "Phiên bàn còn các món đang chờ nhân viên phục vụ xác nhận. Vui lòng xử lý trước khi tạo hóa đơn.");
            }

            var effective = await policies.GetEffectiveAsync(clock.GetUtcNow().UtcDateTime, ct);
            if (effective.Count != 1 || effective[0].VatRate is < 0 or > 1
                || effective[0].ServiceRate is < 0 or > 1)
            {
                throw new BillingException(409, "Cần có một chính sách thuế và phí dịch vụ hợp lệ đang áp dụng (tỷ lệ từ 0 đến 1).");
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
                throw new BillingException(409, "Dữ liệu đơn hàng đã thay đổi trong khi tạo hóa đơn. Vui lòng kiểm tra lại đơn hàng và thử lại.");
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
            throw new BillingException(400, "Mã hóa đơn không hợp lệ hoặc bị trống.");
        }

        var initial = await bills.GetDetailsAsync(id, ct) ?? throw new BillingException(404, "Không tìm thấy hóa đơn.");
        RequireBranch(actor, initial.RestaurantId);
        var initialSessions = initial.BillLines.Select(line => line.TableSessionRef).Distinct().ToArray();
        if (initialSessions.Length != 1)
        {
            throw new BillingException(409, "Hóa đơn phải xác định đúng một phiên bàn.");
        }

        var bill = await unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            if (!await bills.LockSessionAsync(initialSessions[0], ct))
            {
                throw new BillingException(409, "Thanh toán đang bận. Vui lòng thử lại yêu cầu.");
            }

            await RequireCheckout(initialSessions[0], checkoutId, ct);
            var stored = await bills.GetForRefreshAsync(id, ct)
                ?? throw new BillingException(404, "Không tìm thấy hóa đơn.");
            RequireBranch(actor, stored.RestaurantId);
            var verified = await access.GetCurrentScopeAsync(stored.RestaurantId, ct);
            RequireRole(verified, true);
            if (actor.UserId != verified.UserId)
            {
                throw new BillingException(401, "Phiên làm việc của nhân viên đã thay đổi. Vui lòng đăng nhập lại.");
            }

            RequireBranch(verified, stored.RestaurantId);
            if (stored.Status != "Unpaid" || stored.PaidAt.HasValue || stored.Payments.Any(PayOsPaymentRules.IsBlocking))
            {
                throw new BillingException(409, "Chỉ có thể làm mới hóa đơn chưa thanh toán và chưa có giao dịch thanh toán.");
            }

            if (stored.Currency != "VND" || settings.Value.OrderPriceBasis != "Exclusive"
                || stored.DiscountAmount != 0 || stored.BillAdjustments.Count != 0
                || stored.VoucherRedemptions.Count != 0 || stored.BillLines.Any(line => line.DiscountAllocated != 0)
                || stored.VatRateSnapshot is < 0 or > 1 || stored.ServiceRateSnapshot is < 0 or > 1)
            {
                throw new BillingException(409, "Hóa đơn này không thể làm mới theo quy tắc tính giá VND chưa bao gồm thuế phí hiện hành.");
            }

            var sessions = stored.BillLines.Select(line => line.TableSessionRef).Distinct().ToArray();
            if (sessions.Length != 1 || sessions[0] == Guid.Empty
                || stored.BillLines.Select(line => line.OrderItemRef).Distinct().Count() != stored.BillLines.Count)
            {
                throw new BillingException(409, "Hóa đơn phải chứa bản chụp hợp lệ của một phiên bàn và không có món trùng lặp.");
            }

            var sessionId = sessions[0];
            var snapshot = await source.GetBySessionAsync(sessionId, ct);
            ValidateSource(snapshot, sessionId, requireItems: false);
            if (snapshot.RestaurantId != stored.RestaurantId)
            {
                throw new BillingException(409, "Nguồn đơn hàng không thuộc về nhà hàng của hóa đơn này.");
            }

            if (snapshot.PendingOrderCount != 0)
            {
                throw new BillingException(409, "Vui lòng xử lý các đơn hàng đang chờ nhân viên xác nhận trước khi làm mới hóa đơn.");
            }

            var fingerprint = Fingerprint(snapshot);
            var draft = BuildRefresh(stored, snapshot, fingerprint);
            var latest = await source.GetBySessionAsync(sessionId, ct);
            ValidateSource(latest, sessionId, requireItems: false);
            if (Fingerprint(latest) != fingerprint)
            {
                throw new BillingException(409, "Dữ liệu đơn hàng đã thay đổi trong khi làm mới hóa đơn. Vui lòng kiểm tra lại đơn hàng và thử lại.");
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
                throw new BillingException(409, "Bản chụp món ăn đã lưu không thể xác minh để làm mới.");
            }

            if (item.ItemId == Guid.Empty || item.OrderId == Guid.Empty || string.IsNullOrEmpty(item.SourceFingerprint))
            {
                throw new BillingException(409, "Bản chụp món ăn đã lưu không thể xác minh để làm mới.");
            }

            var quantity = 0;
            var price = previous.UnitPriceSnapshot;
            var table = previous.OriginalTableRef;
            var combo = previous.ComboGroupRef;
            if (current.Remove(previous.OrderItemRef, out var line))
            {
                if (item.OrderId != line.Item.OrderId || item.ItemId != line.Item.ItemId)
                {
                    throw new BillingException(409, "Món ăn trong đơn đã bị thay đổi định danh. Vui lòng kiểm tra lại nguồn trước khi làm mới.");
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
            throw new BillingException(400, "Vui lòng cung cấp phân trang hợp lệ, mã nhà hàng (nếu có) và từ khóa tìm kiếm tối đa 50 ký tự.");
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
            throw new BillingException(400, "Mã hóa đơn không hợp lệ hoặc bị trống.");
        }

        var bill = await bills.GetDetailsAsync(id, ct) ?? throw new BillingException(404, "Không tìm thấy hóa đơn.");
        RequireBranch(actor, bill.RestaurantId);
        return await Read(bill, ct);
    }

    private async Task<BillDTO> Read(Bill bill, CancellationToken ct)
    {
        var result = Map(bill);
        var sessions = bill.BillLines.Select(line => line.TableSessionRef).Distinct().ToArray();
        if (sessions.Length != 1 || result.Items.Any(line => string.IsNullOrEmpty(line.Item.SourceFingerprint)))
        {
            result.SourceStatus = "Unverifiable";
            result.Warnings.Add("Hóa đơn đã lưu không chứa bản chụp nguồn phiên bàn hợp lệ.");
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
            result.Warnings.Add("Không thể xác minh tính cập nhật của đơn hàng. Không được thu tiền bằng bản chụp này.");
            return result;
        }
        ValidateSource(current, sessions[0], requireItems: false);
        result.SourceStatus = current.PendingOrderCount == 0 && current.RestaurantId == bill.RestaurantId
            && result.Items.All(line => line.Item.SourceFingerprint == Fingerprint(current)) ? "Current" : "Stale";
        if (result.SourceStatus == "Stale")
        {
            result.Warnings.Add("Dữ liệu đơn hàng đã thay đổi sau khi tạo hóa đơn. Không được thu tiền bằng bản chụp này.");
        }

        return result;
    }

    public async Task<BillDTO> GetPrebill(Guid sessionId, Guid checkoutId, CancellationToken ct)
    {
        if (sessionId == Guid.Empty || checkoutId == Guid.Empty)
        {
            throw new BillingException(400, "Vui lòng cung cấp mã phiên và yêu cầu thanh toán hợp lệ.");
        }

        var snapshot = await source.GetBySessionAsync(sessionId, ct);
        var actor = await access.GetCurrentScopeAsync(snapshot.RestaurantId, ct);
        if (actor.UserId == Guid.Empty || actor.IsAdministrator
            || !roles.Value.WaiterRoleIds.Contains(actor.RoleId, StringComparer.Ordinal)
            || !actor.RestaurantIds.Contains(snapshot.RestaurantId)
            || await paymentRequests.GetReviewedWaiterAsync(sessionId, checkoutId, ct) != actor.UserId)
        {
            throw new BillingException(403, "Chỉ phục vụ phụ trách yêu cầu tại nhà hàng được xem hóa đơn để đưa khách.");
        }

        var checkout = await bills.GetActiveCheckoutAsync(sessionId, ct);
        if (checkout?.CheckoutId != checkoutId || checkout.Status is not ("Ready" or "Paid")
            || !checkout.PreparedAt.HasValue || !checkout.PreparedByRef.HasValue || !checkout.BillId.HasValue)
        {
            throw new BillingException(409, "Thu ngân chưa chuẩn bị xong hóa đơn cho yêu cầu này.");
        }

        var bill = await bills.GetDetailsAsync(checkout.BillId.Value, ct)
            ?? throw new BillingException(404, "Không tìm thấy hóa đơn đã chuẩn bị.");
        if (bill.RestaurantId != snapshot.RestaurantId || bill.BillLines.Any(line => line.TableSessionRef != sessionId))
        {
            throw new BillingException(409, "Hóa đơn không thuộc phiên bàn và nhà hàng của yêu cầu.");
        }

        return await Read(bill, ct);
    }

    private void RequireRole(BillingAccessScope actor, bool create)
    {
        if (actor.UserId == Guid.Empty)
        {
            throw new BillingException(401, "Yêu cầu phiên làm việc của nhân viên hợp lệ.");
        }

        var cashier = roles.Value.CashierRoleIds.Contains(actor.RoleId, StringComparer.Ordinal);
        var manager = roles.Value.ManagerRoleIds.Contains(actor.RoleId, StringComparer.Ordinal);
        if (create ? actor.IsAdministrator || !cashier : !actor.IsAdministrator && !cashier && !manager)
        {
            throw new BillingException(403, create ? "Chỉ thu ngân mới có quyền tạo hoặc làm mới hóa đơn." : "Yêu cầu quyền xem thông tin hóa đơn.");
        }

        if (!actor.IsAdministrator && actor.RestaurantIds.Count == 0)
        {
            throw new BillingException(403, "Yêu cầu được phân công vào nhà hàng đang hoạt động.");
        }
    }

    private static void RequireBranch(BillingAccessScope actor, Guid branch)
    {
        if (!actor.IsAdministrator && !actor.RestaurantIds.Contains(branch))
        {
            throw new BillingException(403, "Yêu cầu được phân công vào nhà hàng này.");
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
            throw new BillingException(503, "Dịch vụ gọi món trả về dữ liệu thanh toán không nhất quán.");
        }

        if (requireItems && snapshot.Items.Count == 0)
        {
            throw new BillingException(409, "Không có món nào đã xác nhận và chưa bị hủy để tính hóa đơn.");
        }
    }

    private static decimal Money(decimal value)
    {
        if (value < 0 || value > MaximumMoney)
        {
            throw new BillingException(409, "Số tiền hóa đơn vượt quá giới hạn lưu trữ decimal(18,2).");
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
            throw new BillingException(409, "Vui lòng tiếp tục thanh toán đang hoạt động để đồng bộ hóa đơn này.");
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
            result.Warnings.Add("Dữ liệu giá vốn nguyên liệu không khả dụng. Không thể tính toán giá vốn và lợi nhuận đầy đủ.");
        }

        if (result.Items.Any(line => line.Quantity > 0 && line.Item.Status != "Served"))
        {
            result.Warnings.Add("Một số món chưa được phục vụ khi tạo hóa đơn này.");
        }

        if (result.Items.Count > 0 && result.Items.All(line => line.Quantity == 0))
        {
            result.Warnings.Add("Tất cả các món đã tính trước đó đều đã bị hủy. Các dòng số lượng bằng 0 được giữ lại để lưu lịch sử phiên.");
        }

        return result;
    }
}
