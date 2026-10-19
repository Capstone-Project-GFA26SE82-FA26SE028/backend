namespace Catalog.Common.DTOs;

public sealed class CreateOptionDTO
{
    public Guid OptionGroupId { get; set; }
    public string? OptionName { get; set; }
    public string? ExtraPrice { get; set; }
}
