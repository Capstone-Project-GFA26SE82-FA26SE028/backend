namespace Catalog.Common.DTOs;

public sealed class CreateItemOptionGroupDTO
{
    public Guid OptionGroupId { get; set; }
    public int? MinSelection { get; set; }
    public int? MaxSelection { get; set; }
}
