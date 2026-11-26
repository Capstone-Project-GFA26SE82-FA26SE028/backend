namespace Restaurant.Common.DTOs;

public sealed class CreateReservationDto
{
    public Guid RestaurantId { get; set; }
    // Kept for older clients; ignored on create because staff select the area when confirming.
    public Guid? RequestedAreaId { get; set; }
    public string? ContactName { get; set; }
    public string? Phone { get; set; }
    public int PartySize { get; set; }
    public string? StartAt { get; set; }
}
