namespace Catalog.Common.DTOs;

public sealed class UpdateItemDTO
{
    public Guid CategoryId { get; set; }
    public string? ItemName { get; set; }
    public string? Description { get; set; }
    public string? ImageUrl { get; set; }
    public string? Version { get; set; }
    public int? PreparationTimeMinutes { get; set; }
}
