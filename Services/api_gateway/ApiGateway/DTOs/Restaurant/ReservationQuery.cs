namespace ApiGateway.DTOs.Restaurant;

public sealed class ReservationQuery
{
    public Guid? RestaurantId { get; set; }
    public Guid? RequestedAreaId { get; set; }
    public Guid? AssignedRestaurantId { get; set; }
    public Guid? AssignedAreaId { get; set; }
    public bool UnassignedOnly { get; set; }
    public string? Status { get; set; }
    public string? From { get; set; }
    public string? To { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}
