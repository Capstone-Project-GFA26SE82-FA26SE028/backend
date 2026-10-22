namespace Restaurant.Common.DTOs;

public sealed class ConfirmReservationDto
{
    public Guid[] TableIds { get; set; } = [];
}
