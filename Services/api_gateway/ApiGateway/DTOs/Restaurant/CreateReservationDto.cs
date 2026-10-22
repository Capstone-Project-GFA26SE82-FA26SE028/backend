using System.Text.Json.Serialization;

namespace ApiGateway.DTOs.Restaurant;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class CreateReservationDto
{
    public Guid RestaurantId { get; set; }
    public Guid RequestedAreaId { get; set; }
    public string? ContactName { get; set; }
    public string? Phone { get; set; }
    public int PartySize { get; set; }
    public string? StartAt { get; set; }
}
