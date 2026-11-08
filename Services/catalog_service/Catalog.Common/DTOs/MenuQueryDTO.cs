namespace Catalog.Common.DTOs;

public sealed class MenuQueryDTO
{
    public Guid? CategoryId { get; set; }
    public string? Search { get; set; }
    public string Channel { get; set; } = "DineIn";
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}
