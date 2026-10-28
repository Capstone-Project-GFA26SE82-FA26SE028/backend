namespace Billing.Common.DTOs;

public sealed class TaxFeePolicyDTO
{
    public Guid TaxFeePolicyId { get; set; }
    public int Version { get; set; }
    public decimal VatRate { get; set; }
    public decimal ServiceRate { get; set; }
    public bool ServiceEnabled { get; set; }
    public DateTimeOffset EffectiveFrom { get; set; }
}
