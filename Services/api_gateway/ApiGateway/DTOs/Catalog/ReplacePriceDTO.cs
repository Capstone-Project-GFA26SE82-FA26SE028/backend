using System.Text.Json.Serialization;

namespace ApiGateway.DTOs.Catalog;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class ReplacePriceDTO
{
    public decimal? Amount { get; set; }
    public string? EffectiveTo { get; set; }
}
