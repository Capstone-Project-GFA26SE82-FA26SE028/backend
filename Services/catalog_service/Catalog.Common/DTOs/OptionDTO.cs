namespace Catalog.Common.DTOs;

public sealed class OptionDTO
{
    public Guid OptionId { get; set; }
    public Guid OptionGroupId { get; set; }
    public string OptionName { get; set; } = string.Empty;
    public decimal ExtraPrice { get; set; }
    public string Availability { get; set; } = string.Empty;
}
