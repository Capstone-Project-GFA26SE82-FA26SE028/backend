namespace ApiGateway.DTOs.Catalog;

public sealed class PriceQueryDTO
{
    public Guid? ItemId { get; set; }
    public string? Channel { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}
