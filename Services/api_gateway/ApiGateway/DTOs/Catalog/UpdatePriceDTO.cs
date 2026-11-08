using System.Text.Json.Serialization;

namespace ApiGateway.DTOs.Catalog;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class UpdatePriceDTO
{
    public string? Channel { get; set; }
    public decimal? Amount { get; set; }
    public string? EffectiveFrom { get; set; }
    public string? EffectiveTo { get; set; }
    public string? DailyStart { get; set; }
    public string? DailyEnd { get; set; }
}
