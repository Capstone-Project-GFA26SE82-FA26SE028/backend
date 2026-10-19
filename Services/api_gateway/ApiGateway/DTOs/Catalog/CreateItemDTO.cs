namespace ApiGateway.DTOs.Catalog;

public sealed class CreateItemDTO
{
    public Guid CategoryId { get; set; }
    public string? ItemName { get; set; }
    public string? Description { get; set; }
    public string? ImageUrl { get; set; }
}
