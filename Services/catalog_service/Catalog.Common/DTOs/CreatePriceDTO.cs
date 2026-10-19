namespace Catalog.Common.DTOs;

public sealed class CreatePriceDTO
{
    public Guid ItemId { get; set; }
    public string? Channel { get; set; }
    public string? Amount { get; set; }
    public string? EffectiveFrom { get; set; }
    public string? EffectiveTo { get; set; }
    public string? DailyStart { get; set; }
    public string? DailyEnd { get; set; }
}
