using System.Text.Json.Serialization;

namespace ApiGateway.DTOs.Billing;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class CreateTaxFeePolicyDTO
{
    public decimal? VatRate { get; set; }
    public decimal? ServiceRate { get; set; }
    public bool? ServiceEnabled { get; set; }
    public string? EffectiveFrom { get; set; }
}

public sealed class TaxFeePolicyDTO
{
    public Guid TaxFeePolicyId { get; set; }
    public int Version { get; set; }
    public decimal VatRate { get; set; }
    public decimal ServiceRate { get; set; }
    public bool ServiceEnabled { get; set; }
    public DateTimeOffset EffectiveFrom { get; set; }
}

public sealed class TaxFeePolicyQueryDTO
{
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}
