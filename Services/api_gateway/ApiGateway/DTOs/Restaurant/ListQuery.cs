namespace ApiGateway.DTOs.Restaurant;

public sealed class ListQuery
{
    public Guid? RestaurantId { get; set; }
    public Guid? AreaId { get; set; }
    public bool? IsActive { get; set; }
    public string? Search { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}
