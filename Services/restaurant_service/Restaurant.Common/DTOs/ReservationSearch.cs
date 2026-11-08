namespace Restaurant.Common.DTOs;

public sealed class ReservationSearch
{
    public IReadOnlyList<Guid>? AllowedRestaurantIds { get; set; }
    public Guid? RestaurantId { get; set; }
    public Guid? RequestedAreaId { get; set; }
    public Guid? AssignedRestaurantId { get; set; }
    public Guid? AssignedAreaId { get; set; }
    public bool UnassignedOnly { get; set; }
    public string? Status { get; set; }
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
}
