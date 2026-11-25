namespace Billing.Common.DTOs;

public sealed record ReportQuantityDTO(decimal KnownQuantity, decimal? Quantity)
{
    public bool IsComplete => Quantity.HasValue;
}

public sealed record DishPriceSnapshotDTO(decimal UnitPrice, IReadOnlyList<BillOptionSnapshot> Options);

public sealed record DishReportItemDTO(Guid ItemId, IReadOnlyList<string> Names,
    ReportQuantityDTO Quantity, ReportAmountDTO GrossSales, ReportAmountDTO NetSales,
    ReportAmountDTO Cost, ReportAmountDTO GrossProfit, IReadOnlyList<DishPriceSnapshotDTO> Prices);

public sealed record RestaurantDishReportDTO(ReportPeriodDTO Period, string Currency, SalesReportFilterDTO Filters,
    Guid? ItemId, bool QuantityComplete, IReadOnlyList<DishReportItemDTO> Items, IReadOnlyList<ReportWarningDTO> Warnings);
