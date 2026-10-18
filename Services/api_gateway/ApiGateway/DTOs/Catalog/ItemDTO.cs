namespace ApiGateway.DTOs.Catalog;

public sealed class ItemDTO
{
    public Guid ItemId { get; set; }
    public Guid CategoryId { get; set; }
    public string CategoryName { get; set; } = string.Empty;
    public string ItemCode { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? ImageUrl { get; set; }
    public string Availability { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
}
