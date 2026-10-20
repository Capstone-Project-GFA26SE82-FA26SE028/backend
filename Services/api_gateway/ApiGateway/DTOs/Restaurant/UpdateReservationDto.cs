using System.Text.Json.Serialization;

namespace ApiGateway.DTOs.Restaurant;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class UpdateReservationDto
{
    public string? StartAt { get; set; }
    public Guid[] TableIds { get; set; } = [];
}
