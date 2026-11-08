namespace Billing.Common.DTOs;

public sealed class CreateTaxFeePolicyDTO
{
    public decimal? VatRate { get; set; }
    public decimal? ServiceRate { get; set; }
    public bool? ServiceEnabled { get; set; }
    public string? EffectiveFrom { get; set; }
}
