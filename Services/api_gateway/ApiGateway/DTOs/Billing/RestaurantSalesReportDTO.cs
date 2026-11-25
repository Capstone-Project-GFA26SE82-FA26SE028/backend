namespace ApiGateway.DTOs.Billing;

// KnownAmount is the supported subtotal; Amount is null when the full metric cannot be established.
public sealed record ReportAmountDTO(decimal KnownAmount, decimal? Amount)
{
    public bool IsComplete => Amount.HasValue;
}

public sealed record ReportCountDTO(int KnownCount, int? Count)
{
    public bool IsComplete => Count.HasValue;
}

public sealed record ReportWarningDTO(string Code, string Message);

public sealed record SalesReportTotalsDTO(
    ReportAmountDTO Collections,
    ReportAmountDTO Refunds,
    ReportAmountDTO NetCollections,
    ReportAmountDTO DishRevenue,
    ReportAmountDTO NetDishRevenue,
    ReportAmountDTO Discounts,
    ReportAmountDTO Vat,
    ReportAmountDTO ServiceFees,
    ReportCountDTO PaidOrders,
    ReportAmountDTO RoundingAdjustment);

public sealed record SalesReportGroupDTO(string Key, SalesReportTotalsDTO Totals);

public sealed record RestaurantSalesReportDTO(
    ReportPeriodDTO Period,
    string Currency,
    SalesReportTotalsDTO Totals,
    IReadOnlyList<SalesReportGroupDTO> Groups,
    IReadOnlyList<ReportWarningDTO> Warnings,
    IReadOnlyList<SalesReportGroupDTO> Channels,
    IReadOnlyList<SalesReportGroupDTO> Methods,
    IReadOnlyList<SalesReportGroupDTO> Shifts,
    SalesReportFilterDTO Filters);
