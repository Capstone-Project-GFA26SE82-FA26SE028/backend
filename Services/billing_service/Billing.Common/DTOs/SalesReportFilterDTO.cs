namespace Billing.Common.DTOs;

public sealed class SalesReportFilterDTO
{
    public string Channel { get; set; } = "all";
    public string MethodCode { get; set; } = "all";
    public string Shift { get; set; } = "all";
}
