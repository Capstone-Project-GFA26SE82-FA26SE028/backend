namespace Catalog.Common.DTOs;

public sealed class ItemOptionGroupDTO
{
    public Guid ItemId { get; set; }
    public Guid OptionGroupId { get; set; }
    public string OptionGroupName { get; set; } = string.Empty;
    public int MinSelection { get; set; }
    public int MaxSelection { get; set; }
}
