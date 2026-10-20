namespace ApiGateway.DTOs.Restaurant;

public sealed class ReservationAvailabilityQuery
{
    public Guid RestaurantId { get; set; }
    public Guid AreaId { get; set; }
    public int PartySize { get; set; }
    public string? StartAt { get; set; }
}
