using System.Text.Json.Serialization;

namespace Restaurant.Common.DTOs;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class UpdateReservationDto
{
    public string? StartAt { get; set; }
    public Guid[] TableIds { get; set; } = [];
}
