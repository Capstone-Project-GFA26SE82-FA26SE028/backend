namespace Catalog.Common.DTOs;

public sealed class PriceDTO
{
    public Guid PriceId { get; set; }
    public Guid ItemId { get; set; }
    public string Channel { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public DateTimeOffset EffectiveFrom { get; set; }
    public DateTimeOffset? EffectiveTo { get; set; }
    public TimeOnly? DailyStart { get; set; }
    public TimeOnly? DailyEnd { get; set; }
}
