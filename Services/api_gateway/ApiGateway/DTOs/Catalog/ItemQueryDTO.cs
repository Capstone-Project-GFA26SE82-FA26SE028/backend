namespace ApiGateway.DTOs.Catalog;

public sealed class ItemQueryDTO
{
    public Guid? CategoryId { get; set; }
    public string? Search { get; set; }
    public string? Availability { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}
