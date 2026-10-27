namespace ApiGateway.DTOs.Billing;

public sealed class BillQueryDTO
{
    public Guid? RestaurantId { get; set; }
    public string? Search { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}
