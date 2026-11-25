using System.Text.Json;
using Billing.BLL.Interfaces;
using Billing.Common.Constants;
using Billing.Common.DTOs;
using Billing.Common.Exceptions;
using Billing.Common.Settings;
using Billing.DAL.Interfaces;
using Billing.DAL.Models;
using Microsoft.Extensions.Options;

namespace Billing.BLL.Implements;

public sealed class RestaurantDishReportService(IRestaurantReportService periods, IRestaurantReportRepository repository,
    IRestaurantReportItemsClient orders, IOptions<CashPaymentSettings> cash) : IRestaurantDishReportService
{
    public async Task<RestaurantDishReportDTO> GetDishes(Guid restaurantId, ReportPeriodQueryDTO query,
        SalesReportFilterDTO filters, Guid? itemId, CancellationToken ct)
    {
        var period = await periods.GetPeriod(restaurantId, query, ct);
        if (itemId == Guid.Empty)
        {
            throw new BillingException(400, "Mã món lọc không được rỗng.");
        }

        filters = ReportFilterRules.Normalize(filters, cash.Value.MethodCode);
        var source = await repository.GetSourceAsync(restaurantId, period.FromUtc.UtcDateTime, period.ToUtc.UtcDateTime, ct, filters);
        var warnings = new Dictionary<string, ReportWarningDTO>();
        if (filters.Channel != "all")
        {
            Warn(warnings, "ExclusiveChannelFilter", "Bộ lọc chỉ lấy đúng nhóm kênh của hóa đơn; Mixed/Unclassified không thuộc DineIn/Takeaway thuần. Chọn all để xem toàn bộ nguồn món.");
        }

        var rankingComplete = source.MissingPaymentTimes == 0;
        var unknownItemGap = !rankingComplete;
        var incompleteItems = new HashSet<Guid>();
        void MarkGap(Guid? affectedItem = null)
        {
            rankingComplete = false;
            if (affectedItem.HasValue && affectedItem != Guid.Empty)
            {
                incompleteItems.Add(affectedItem.Value);
            }
            else
            {
                unknownItemGap = true;
            }
        }

        if (!rankingComplete)
        {
            Warn(warnings, "MissingPaymentTime", "Có thanh toán thiếu ngày xác nhận; chưa thể khẳng định đủ món trong kỳ.");
        }

        var ids = source.Payments.SelectMany(payment => payment.Bill.BillLines.Where(line => line.Quantity > 0)
                .Select(line => Snapshot(line)?.OrderId ?? Guid.Empty))
            .Concat(source.OnlineSales.Select(value => value.OrderId)).Where(id => id != Guid.Empty).Distinct().ToArray();
        var snapshots = ids.Length == 0 ? new Dictionary<Guid, ReportOrderSnapshotDTO>()
            : await orders.GetItemsAsync(restaurantId, ids, ct);
        var channels = snapshots.ToDictionary(value => value.Key, value => value.Value.Channel);
        var rows = new List<DishReportEntry>();
        var duplicateTransactions = source.Payments.Select(value => value.ProviderTransactionId)
            .Concat(source.OnlineSales.Select(value => value.TransactionReference)).Where(value => !string.IsNullOrWhiteSpace(value))
            .GroupBy(value => value, StringComparer.Ordinal).Where(group => group.Count() > 1).Select(group => group.Key).ToHashSet();
        foreach (var payment in source.Payments)
        {
            if (!ReportPaymentValidation.DineIn(payment, cash.Value.MethodCode)
                || duplicateTransactions.Contains(payment.ProviderTransactionId))
            {
                MarkGap();
                Warn(warnings, "UnverifiedDishPayment", "Có thanh toán chưa đủ chứng cứ hoặc trùng tham chiếu; chưa tính món của giao dịch đó.");
                continue;
            }

            var positive = payment.Bill.BillLines.Where(line => line.Quantity > 0).ToArray();
            if (positive.Length == 0 || payment.Bill.BillLines.Any(line => line.Quantity < 0))
            {
                if (positive.Length == 0)
                {
                    MarkGap();
                }

                foreach (var invalid in payment.Bill.BillLines.Where(line => line.Quantity < 0))
                {
                    MarkGap(Snapshot(invalid)?.ItemId);
                }

                Warn(warnings, "InvalidDishSnapshot", "Hóa đơn đã thu có số lượng dòng không nhất quán.");
            }

            var netSnapshotValid = BillReportValidation.RevenueMatches(payment.Bill);
            if (!netSnapshotValid)
            {
                Warn(warnings, "InconsistentBillRevenue", "Snapshot tổng/giảm giá/phân bổ bill không khớp; doanh thu sau giảm giá và lợi nhuận chưa xác định.");
            }

            var orderIds = positive.Select(line => Snapshot(line)?.OrderId ?? Guid.Empty).ToArray();
            var channel = ReportFilterRules.Channel(orderIds, channels);
            if (!Matches(channel, filters, warnings, ref rankingComplete, ref unknownItemGap))
            {
                continue;
            }

            foreach (var line in positive)
            {
                var snapshot = Snapshot(line);
                try
                {
                    if (snapshot is null || snapshot.ItemId == Guid.Empty || snapshot.OrderId == Guid.Empty
                        || string.IsNullOrWhiteSpace(snapshot.Name) || line.OrderItemRef == Guid.Empty
                        || line.UnitPriceSnapshot < 0 || snapshot.BaseUnitPrice < 0 || !ValidOptions(snapshot.Options)
                        || decimal.Round(snapshot.BaseUnitPrice + snapshot.Options.Sum(value => value.ExtraPrice * value.Quantity),
                            2, MidpointRounding.AwayFromZero) != line.UnitPriceSnapshot)
                    {
                        MarkGap(snapshot?.ItemId);
                        Warn(warnings, "InvalidDishSnapshot", "Snapshot món hoặc giá/option lịch sử không nhất quán; không thay bằng Catalog hiện tại.");
                        continue;
                    }

                    ReportOrderItemDTO? current = null;
                    if (snapshots.TryGetValue(snapshot.OrderId, out var order))
                    {
                        if (Excluded(order.Status, warnings))
                        {
                            continue;
                        }

                        current = Eligible(order.Status) ? order.Items.SingleOrDefault(value => value.OrderItemId == line.OrderItemRef) : null;
                    }

                    var quantity = current is not null && current.ItemId == snapshot.ItemId ? Remaining(current) : null;
                    if (quantity.HasValue)
                    {
                        quantity = Math.Min(line.Quantity, quantity.Value);
                    }
                    else
                    {
                        MarkGap(snapshot.ItemId);
                        Warn(warnings, "DishSourceUnavailable", "Một số dòng món thiếu hoặc sai nguồn lịch sử; kiểm tra cờ đầy đủ của từng món và bảng.");
                    }

                    if (quantity == 0)
                    {
                        continue;
                    }

                    var sameQuantity = quantity == line.Quantity;
                    decimal? net = null;
                    if (netSnapshotValid && quantity.HasValue && line.DiscountAllocated >= 0 && line.DiscountAllocated <= line.Quantity * line.UnitPriceSnapshot
                        && (sameQuantity || line.DiscountAllocated == 0))
                    {
                        net = quantity.Value * line.UnitPriceSnapshot - (sameQuantity ? line.DiscountAllocated : 0);
                    }
                    else
                    {
                        Warn(warnings, "DishDiscountUnavailable", "Chưa có phân bổ giảm giá hợp lệ cho phần số lượng còn bán; không tự chia giảm giá.");
                    }

                    var cost = sameQuantity && line.CogsSnapshot >= 0 ? line.CogsSnapshot : null;
                    if (!cost.HasValue)
                    {
                        Warn(warnings, "DishCostUnavailable", "Giá vốn tổng dòng thiếu hoặc số lượng đã thay đổi; không tự chia giá vốn hay lấy giá bán làm giá vốn.");
                    }

                    rows.Add(new DishReportEntry(line.OrderItemRef, snapshot.ItemId, snapshot.Name,
                        quantity, line.UnitPriceSnapshot, snapshot.Options, net, cost));
                }
                catch (OverflowException)
                {
                    MarkGap(snapshot?.ItemId);
                    Warn(warnings, "InvalidDishSnapshot", "Dữ liệu snapshot vượt giới hạn số học; chưa tính vào phần có bằng chứng.");
                }
            }
        }

        foreach (var payment in source.OnlineSales)
        {
            if (!ReportPaymentValidation.OnlineSale(payment) || duplicateTransactions.Contains(payment.TransactionReference))
            {
                MarkGap();
                Warn(warnings, "UnverifiedDishPayment", "Có thanh toán chưa đủ chứng cứ hoặc trùng tham chiếu; chưa tính món của giao dịch đó.");
                continue;
            }

            if (!Matches("Takeaway", filters, warnings, ref rankingComplete, ref unknownItemGap))
            {
                continue;
            }

            if (!snapshots.TryGetValue(payment.OrderId, out var order))
            {
                MarkGap();
                Warn(warnings, "DishSourceUnavailable", "Thiếu đơn mang về lịch sử; không suy đoán món hoặc số lượng từ số tiền thanh toán.");
                continue;
            }

            if (Excluded(order.Status, warnings))
            {
                continue;
            }

            if (order.Channel != "Takeaway" || !Eligible(order.Status) || order.Items.Count == 0)
            {
                MarkGap();
                Warn(warnings, "DishSourceUnavailable", "Đơn mang về chưa có snapshot hợp lệ để thống kê món.");
                continue;
            }

            if (!OriginalQuoteMatches(order, payment.Amount))
            {
                MarkGap();
                Warn(warnings, "TakeawaySnapshotMismatch", "Tổng giá/option snapshot gốc không khớp số tiền thu theo luồng mang về hiện có; chưa tính món từ nguồn không nhất quán.");
                continue;
            }

            foreach (var item in order.Items)
            {
                var quantity = Remaining(item);
                if (quantity == 0)
                {
                    continue;
                }

                if (!quantity.HasValue || item.OrderItemId == Guid.Empty || item.ItemId == Guid.Empty
                    || string.IsNullOrWhiteSpace(item.Name) || item.UnitPrice < 0 || !ValidOptions(item.Options))
                {
                    MarkGap(item.ItemId);
                    Warn(warnings, "DishSourceUnavailable", "Snapshot dòng món mang về không hợp lệ.");
                    continue;
                }

                try
                {
                    var price = item.UnitPrice + item.Options.Sum(value => value.Quantity * value.ExtraPrice);
                    rows.Add(new DishReportEntry(item.OrderItemId, item.ItemId, item.Name, quantity,
                        price, item.Options, null, null));
                    Warn(warnings, "TakeawayDishFinancialsUnavailable", "Mang về chưa có snapshot giảm giá và giá vốn lịch sử; chỉ xác định lượng và giá bán gộp từ snapshot đơn.");
                }
                catch (OverflowException)
                {
                    MarkGap(item.ItemId);
                    Warn(warnings, "InvalidDishSnapshot", "Giá snapshot vượt giới hạn số học.");
                }
            }
        }

        var duplicateLines = rows.GroupBy(value => value.LineId).Where(group => group.Count() > 1).Select(group => group.Key).ToHashSet();
        if (duplicateLines.Count > 0)
        {
            foreach (var duplicate in rows.Where(value => duplicateLines.Contains(value.LineId)))
            {
                MarkGap(duplicate.ItemId);
            }

            rows = rows.Where(value => !duplicateLines.Contains(value.LineId)).ToList();
            Warn(warnings, "DuplicateDishSource", "Dòng món xuất hiện nhiều lần giữa các nguồn thanh toán; không cộng trùng hoặc tự chọn một bản.");
        }

        try
        {
            var selected = rows.Where(value => !itemId.HasValue || value.ItemId == itemId).ToArray();
            var selectedComplete = itemId.HasValue
                ? !unknownItemGap && !incompleteItems.Contains(itemId.Value) && selected.All(value => value.Quantity.HasValue)
                : rankingComplete;
            return new RestaurantDishReportDTO(period, "VND", filters, itemId, selectedComplete,
                DishReportAggregation.Build(selected, !unknownItemGap, incompleteItems), warnings.Values.OrderBy(value => value.Code, StringComparer.Ordinal).ToArray());
        }
        catch (OverflowException)
        {
            throw new BillingException(503, "Tổng dữ liệu báo cáo vượt giới hạn số học, cần kiểm tra dữ liệu nguồn.");
        }
    }

    private static bool OriginalQuoteMatches(ReportOrderSnapshotDTO order, decimal amount)
    {
        try
        {
            return order.Items.All(value => value.Quantity > 0 && value.UnitPrice >= 0 && ValidOptions(value.Options))
                && order.Items.Sum(value => value.Quantity * (value.UnitPrice + value.Options.Sum(option => option.Quantity * option.ExtraPrice))) == amount;
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    private static void Warn(IDictionary<string, ReportWarningDTO> warnings, string code, string message)
    {
        warnings.TryAdd(code, new ReportWarningDTO(code, message));
    }

    private static bool Matches(string channel, SalesReportFilterDTO filter, IDictionary<string, ReportWarningDTO> warnings, ref bool rankingComplete, ref bool unknownItemGap)
    {
        if (channel == "Unclassified")
        {
            Warn(warnings, "OrderChannelUnavailable", "Thiếu nguồn kênh của hóa đơn, không tự coi là DineIn.");
            if (filter.Channel is not ("all" or "Unclassified"))
            {
                rankingComplete = false;
                unknownItemGap = true;
            }
        }

        return filter.Channel == "all" || filter.Channel == channel;
    }

    private static BillItemSnapshot? Snapshot(BillLine line)
    {
        try
        {
            return JsonSerializer.Deserialize<BillItemSnapshot>(line.ItemSnapshot);
        }
        catch (Exception error) when (error is JsonException or ArgumentException)
        {
            return null;
        }
    }

    private static bool Eligible(string status)
    {
        return status is "Confirmed" or "Cooking" or "Ready" or "Served" or "Accepted" or "ReadyForPickup" or "Completed";
    }

    private static bool Excluded(string status, IDictionary<string, ReportWarningDTO> warnings)
    {
        if (status is "Cancelled" or "Rejected")
        {
            return true;
        }

        if (status is "PendingPayment" or "PendingConfirmation")
        {
            Warn(warnings, "PendingDishesExcluded", "Đơn chưa được nhân viên xác nhận chưa được tính vào lượng bán hợp lệ.");
            return true;
        }

        return false;
    }

    private static decimal? Remaining(ReportOrderItemDTO item)
    {
        if (item.Quantity <= 0 || item.CancelledQuantity < 0 || item.CancelledQuantity > item.Quantity)
        {
            return null;
        }

        return item.Status == "Cancelled" ? 0 : item.Quantity - item.CancelledQuantity;
    }

    private static bool ValidOptions(IEnumerable<BillOptionSnapshot> options)
    {
        return options.All(value => value.OptionId != Guid.Empty && value.Quantity > 0 && value.ExtraPrice >= 0);
    }
}
