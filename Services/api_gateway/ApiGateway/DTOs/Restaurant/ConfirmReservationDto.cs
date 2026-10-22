namespace ApiGateway.DTOs.Restaurant;

public sealed class ConfirmReservationDto
{
    public Guid[] TableIds { get; set; } = [];
}
