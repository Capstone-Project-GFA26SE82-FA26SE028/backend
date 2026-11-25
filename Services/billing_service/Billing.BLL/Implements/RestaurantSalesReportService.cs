using System.Globalization;
using System.Text.Json;
using Billing.Common.Constants;
using Billing.Common.Exceptions;
using Billing.BLL.Interfaces;
using Billing.Common.DTOs;
using Billing.Common.Settings;
using Billing.DAL.Interfaces;
using Billing.DAL.Models;
using Microsoft.Extensions.Options;

namespace Billing.BLL.Implements;

public sealed class RestaurantSalesReportService(IRestaurantReportService periods,
    IRestaurantReportRepository repository, IOptions<CashPaymentSettings> cash, IRestaurantReportOrderClient orderChannels) : IRestaurantSalesReportService
{
    public async Task<RestaurantSalesReportDTO> GetSales(Guid restaurantId, ReportPeriodQueryDTO query, CancellationToken ct, SalesReportFilterDTO? filters = null)
    {
        var period = await periods.GetPeriod(restaurantId, query, ct);
        filters = ReportFilterRules.Normalize(filters, cash.Value.MethodCode);
        var source = await repository.GetSourceAsync(restaurantId, period.FromUtc.UtcDateTime, period.ToUtc.UtcDateTime, ct, filters);
        var warnings = new Dictionary<string, ReportWarningDTO>();
        Warn(warnings, "DineInRefundsUnavailable", "Chưa có nguồn xác minh hoàn tiền tại bàn; tổng hoàn và thực thu ròng đầy đủ chưa xác định.");
        var entries = new List<Entry>();
        var ids = source.Payments.SelectMany(value => OrderIds(value.Bill) ?? []).Distinct().ToArray();
        var channels = ids.Length == 0 ? new Dictionary<Guid, string>()
            : await orderChannels.GetChannelsAsync(restaurantId, ids, ct);
        var collectionsComplete = source.MissingPaymentTimes == 0;
        if (!collectionsComplete)
        {
            Warn(warnings, "MissingPaymentTime", "Có khoản thanh toán thiếu thời điểm xác nhận, chưa thể phân bổ vào kỳ.");
        }

        if (source.MissingRefundTimes > 0 || source.UnverifiedDineInRefunds > 0)
        {
            Warn(warnings, "UnverifiedRefundRecords", "Có bản ghi hoàn tiền thiếu nguồn xác minh hoặc thời điểm hợp lệ; chưa tính vào số đã xác minh.");
        }

        var duplicatedTransactions = source.Payments.Where(value => !string.IsNullOrWhiteSpace(value.ProviderTransactionId))
            .Select(value => value.ProviderTransactionId)
            .Concat(source.OnlineSales.Where(value => !string.IsNullOrWhiteSpace(value.TransactionReference))
                .Select(value => value.TransactionReference!))
            .GroupBy(value => value, StringComparer.Ordinal).Where(group => group.Count() > 1)
            .Select(group => group.Key).ToHashSet(StringComparer.Ordinal);
        foreach (var payment in source.Payments)
        {
            if (!ReportPaymentValidation.DineIn(payment, cash.Value.MethodCode)
                || duplicatedTransactions.Contains(payment.ProviderTransactionId ?? string.Empty))
            {
                collectionsComplete = false;
                Warn(warnings, "UnverifiedCollection", "Có giao dịch trong kỳ chưa đủ chứng cứ hoặc trùng tham chiếu; không cộng vào số đã xác minh.");
                continue;
            }

            var revenueComplete = BillReportValidation.RevenueMatches(payment.Bill);
            var orders = OrderIds(payment.Bill);
            if (!revenueComplete)
            {
                Warn(warnings, "InconsistentBillRevenue", "Snapshot tiền món, giảm giá hoặc phân bổ dòng không nhất quán; chưa tính doanh thu đầy đủ.");
            }

            if (orders is null)
            {
                Warn(warnings, "MissingOrderSnapshot", "Snapshot hóa đơn thiếu mã đơn hợp lệ; không dùng mã hóa đơn thay mã đơn.");
            }

            entries.Add(new Entry(
                At: Utc(payment.PaidAt!.Value),
                Collected: payment.Amount,
                Revenue: revenueComplete ? payment.Bill.SubTotal - payment.Bill.DiscountAmount : 0,
                Discount: revenueComplete ? payment.Bill.DiscountAmount : 0,
                Vat: revenueComplete ? payment.Bill.VatAmount : 0,
                Service: revenueComplete ? payment.Bill.ServiceAmount : 0,
                Rounding: revenueComplete ? payment.Bill.TotalAmount - (payment.Bill.SubTotal - payment.Bill.DiscountAmount
                    + payment.Bill.VatAmount + payment.Bill.ServiceAmount) : 0,
                RevenueComplete: revenueComplete,
                OrderIds: orders,
                Channel: ReportFilterRules.Channel(orders, channels),
                Method: payment.MethodCode,
                ShiftId: payment.CashShiftId));
        }

        foreach (var payment in source.OnlineSales)
        {
            if (!ReportPaymentValidation.OnlineSale(payment)
                || duplicatedTransactions.Contains(payment.TransactionReference ?? string.Empty))
            {
                collectionsComplete = false;
                Warn(warnings, "UnverifiedCollection", "Có giao dịch trong kỳ chưa đủ chứng cứ hoặc trùng tham chiếu; không cộng vào số đã xác minh.");
                continue;
            }

            entries.Add(new Entry(
                At: Utc(payment.PaidAt!.Value),
                Collected: payment.Amount,
                RevenueComplete: false,
                OrderIds: [payment.OrderId],
                Channel: "Takeaway",
                Method: PayOsStates.Method,
                IsOnline: true));
            Warn(warnings, "TakeawayRevenueBreakdownUnavailable", "Thanh toán mang về chưa có snapshot tách tiền món, VAT, phí và giảm giá; chỉ ghi nhận tiền thu đã xác minh.");
        }

        foreach (var payment in source.OnlineRefunds)
        {
            if (!ReportPaymentValidation.OnlineRefund(payment))
            {
                Warn(warnings, "UnverifiedRefundRecords", "Có bản ghi hoàn tiền thiếu nguồn xác minh hoặc thời điểm hợp lệ; chưa tính vào số đã xác minh.");
                continue;
            }

            entries.Add(new Entry(
                At: Utc(payment.RefundedAt!.Value),
                Refunded: payment.Amount,
                RevenueComplete: false,
                OrderIds: [],
                Channel: "Takeaway",
                Method: PayOsStates.Method,
                IsOnline: true));
            Warn(warnings, "RefundRevenueBreakdownUnavailable", "Đã xác minh tiền hoàn nhưng chưa có phần tiền món/VAT/phí được hoàn; chưa xác định doanh thu ròng đầy đủ.");
        }

        if (entries.Any(value => value.Channel == "Unclassified"))
        {
            Warn(warnings, "OrderChannelUnavailable", "Thiếu nguồn kênh của một số hóa đơn; khoản thu giữ ở Unclassified, không suy đoán DineIn/Takeaway.");
        }

        if (filters.Channel != "all")
        {
            if (entries.Any(value => value.Channel == "Unclassified") && filters.Channel != "Unclassified")
            {
                collectionsComplete = false;
            }

            Warn(warnings, "ExclusiveChannelFilter", "Bộ lọc chỉ lấy đúng nhóm kênh được chọn; Mixed và Unclassified không thuộc DineIn/Takeaway thuần. Xem all để đối chiếu toàn bộ khoản thu.");
            entries = entries.Where(value => value.Channel == filters.Channel).ToList();
        }

        var zone = TimeZoneInfo.FindSystemTimeZoneById(period.TimeZone);
        var groups = entries.GroupBy(value => Key(value.At, zone, period.GroupBy))
            .OrderBy(group => group.Min(value => value.At))
            .Select(group => new SalesReportGroupDTO(group.Key, Totals(group.ToArray(), collectionsComplete)))
            .ToArray();
        return new RestaurantSalesReportDTO(period, "VND", Totals(entries, collectionsComplete), groups,
            warnings.Values.OrderBy(value => value.Code, StringComparer.Ordinal).ToArray(),
            ChannelBreakdown(entries, collectionsComplete),
            Breakdown(entries, value => value.Method, collectionsComplete),
            Breakdown(entries, value => value.ShiftId?.ToString() ?? "unassigned", collectionsComplete),
            filters);
    }

    private static SalesReportGroupDTO[] ChannelBreakdown(IReadOnlyCollection<Entry> entries, bool collectionsComplete)
    {
        var hasUnclassified = entries.Any(value => value.Channel == "Unclassified");
        return entries.GroupBy(value => value.Channel).OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => new SalesReportGroupDTO(group.Key,
                Totals(group.ToArray(), collectionsComplete && (!hasUnclassified || group.Key == "Unclassified"))))
            .ToArray();
    }

    private static SalesReportGroupDTO[] Breakdown(IEnumerable<Entry> entries, Func<Entry, string> key, bool complete)
    {
        return entries.GroupBy(key).OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => new SalesReportGroupDTO(group.Key, Totals(group.ToArray(), complete))).ToArray();
    }

    private static SalesReportTotalsDTO Totals(IReadOnlyCollection<Entry> entries, bool collectionsComplete)
    {
        var collections = entries.Sum(value => value.Collected);
        var refunds = entries.Sum(value => value.Refunded);
        var revenue = entries.Sum(value => value.Revenue);
        var revenueComplete = collectionsComplete && entries.Where(value => value.Collected > 0).All(value => value.RevenueComplete);
        var ordersComplete = collectionsComplete && entries.All(value => value.OrderIds is not null);
        var orderCount = entries.SelectMany(value => (value.OrderIds ?? []).Select(id => (value.IsOnline, id))).Distinct().Count();
        return new SalesReportTotalsDTO(
            new ReportAmountDTO(collections, collectionsComplete ? collections : null),
            new ReportAmountDTO(refunds, null),
            new ReportAmountDTO(collections - refunds, null),
            new ReportAmountDTO(revenue, revenueComplete ? revenue : null),
            new ReportAmountDTO(revenue, null),
            new ReportAmountDTO(entries.Sum(value => value.Discount), revenueComplete ? entries.Sum(value => value.Discount) : null),
            new ReportAmountDTO(entries.Sum(value => value.Vat), revenueComplete ? entries.Sum(value => value.Vat) : null),
            new ReportAmountDTO(entries.Sum(value => value.Service), revenueComplete ? entries.Sum(value => value.Service) : null),
            new ReportCountDTO(orderCount, ordersComplete ? orderCount : null),
            new ReportAmountDTO(entries.Sum(value => value.Rounding), revenueComplete ? entries.Sum(value => value.Rounding) : null));
    }

    private static Guid[]? OrderIds(Bill bill)
    {
        try
        {
            var lines = bill.BillLines.Where(value => value.Quantity > 0).ToArray();
            var ids = lines.Select(value => JsonSerializer.Deserialize<BillItemSnapshot>(value.ItemSnapshot)?.OrderId ?? Guid.Empty).ToArray();
            return lines.Length > 0 && ids.All(value => value != Guid.Empty) ? ids : null;
        }
        catch (Exception error) when (error is JsonException or ArgumentException)
        {
            return null;
        }
    }

    private static DateTimeOffset Utc(DateTime value)
    {
        return new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc));
    }

    private static string Key(DateTimeOffset instant, TimeZoneInfo zone, string grouping)
    {
        var local = TimeZoneInfo.ConvertTime(instant, zone);
        if (grouping == "hour")
        {
            return new DateTimeOffset(local.Year, local.Month, local.Day, local.Hour, 0, 0, local.Offset)
                .ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture);
        }

        var date = local.Date;
        if (grouping == "week")
        {
            date = date.AddDays(-(((int)date.DayOfWeek + 6) % 7));
        }
        else if (grouping == "month")
        {
            date = new DateTime(date.Year, date.Month, 1);
        }

        return date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    private static void Warn(IDictionary<string, ReportWarningDTO> warnings, string code, string message)
    {
        warnings.TryAdd(code, new ReportWarningDTO(code, message));
    }

    private sealed record Entry(DateTimeOffset At, decimal Collected = 0, decimal Refunded = 0,
        decimal Revenue = 0, decimal Discount = 0, decimal Vat = 0, decimal Service = 0, decimal Rounding = 0,
        bool RevenueComplete = true, Guid[]? OrderIds = null,
        string Channel = "", string Method = "", Guid? ShiftId = null, bool IsOnline = false);
}
