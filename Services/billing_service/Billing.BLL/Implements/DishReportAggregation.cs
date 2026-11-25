using System.Text.Json;
using Billing.Common.DTOs;

namespace Billing.BLL.Implements;

internal sealed record DishReportEntry(Guid LineId, Guid ItemId, string Name, decimal? Quantity,
    decimal UnitPrice, IReadOnlyList<BillOptionSnapshot> Options, decimal? NetSales, decimal? Cost);

internal static class DishReportAggregation
{
    public static DishReportItemDTO[] Build(IEnumerable<DishReportEntry> entries, bool itemCoverageKnown, IReadOnlySet<Guid> incompleteItems)
    {
        return entries.GroupBy(value => value.ItemId).Select(group => Item(group.ToArray(), itemCoverageKnown && !incompleteItems.Contains(group.Key)))
            .OrderByDescending(value => value.Quantity.KnownQuantity).ThenBy(value => value.ItemId).ToArray();
    }

    private static DishReportItemDTO Item(DishReportEntry[] rows, bool complete)
    {
        var quantity = rows.Sum(value => value.Quantity ?? 0);
        var quantityComplete = complete && rows.All(value => value.Quantity.HasValue);
        var gross = rows.Sum(value => (value.Quantity ?? 0) * value.UnitPrice);
        var net = rows.Sum(value => value.NetSales ?? 0);
        var cost = rows.Sum(value => value.Cost ?? 0);
        var discounts = rows.Where(value => value.Quantity.HasValue && value.NetSales.HasValue)
            .Sum(value => value.Quantity!.Value * value.UnitPrice - value.NetSales!.Value);
        var profit = rows.Where(value => value.NetSales.HasValue && value.Cost.HasValue)
            .Sum(value => value.NetSales!.Value - value.Cost!.Value);
        var profitComplete = quantityComplete && rows.All(value => value.NetSales.HasValue && value.Cost.HasValue);
        decimal? margin = profitComplete && net != 0 ? profit / net * 100m : null;
        return new DishReportItemDTO(rows[0].ItemId,
            rows.Select(value => value.Name).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
            new ReportQuantityDTO(quantity, quantityComplete ? quantity : null),
            new ReportAmountDTO(gross, quantityComplete ? gross : null),
            new ReportAmountDTO(net, quantityComplete && rows.All(value => value.NetSales.HasValue) ? net : null),
            new ReportAmountDTO(cost, quantityComplete && rows.All(value => value.Cost.HasValue) ? cost : null),
            new ReportAmountDTO(profit, quantityComplete && rows.All(value => value.NetSales.HasValue && value.Cost.HasValue) ? profit : null),
            rows.Select(value => new DishPriceSnapshotDTO(value.UnitPrice, value.Options.OrderBy(option => option.OptionId).ToArray()))
                .DistinctBy(value => JsonSerializer.Serialize(value)).OrderBy(value => value.UnitPrice).ToArray(),
            new ReportAmountDTO(discounts, quantityComplete && rows.All(value => value.NetSales.HasValue) ? discounts : null), margin);
    }
}
