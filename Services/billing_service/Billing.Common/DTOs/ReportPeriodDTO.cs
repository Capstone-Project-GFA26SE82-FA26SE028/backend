namespace Billing.Common.DTOs;

public sealed class ReportPeriodQueryDTO
{
    public string? From { get; set; }
    public string? To { get; set; }
    public string? GroupBy { get; set; } = "day";
}

public sealed record RestaurantReportContextDTO(Guid RestaurantId, string TimeZone);

public sealed record ReportPeriodDTO(
    Guid RestaurantId,
    string TimeZone,
    DateTimeOffset FromUtc,
    DateTimeOffset ToUtc,
    DateTimeOffset FromLocal,
    DateTimeOffset ToLocal,
    string GroupBy,
    string WeekStartsOn);
