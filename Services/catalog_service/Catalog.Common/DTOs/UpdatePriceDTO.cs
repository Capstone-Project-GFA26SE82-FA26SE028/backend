namespace Catalog.Common.DTOs;

public sealed class UpdatePriceDTO
{
    public string? Channel { get; set; }
    public string? Amount { get; set; }
    public string? EffectiveFrom { get; set; }
    public string? EffectiveTo { get; set; }
    public string? DailyStart { get; set; }
    public string? DailyEnd { get; set; }
}
