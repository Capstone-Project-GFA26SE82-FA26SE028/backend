namespace ApiGateway.DTOs.Restaurant;

public sealed class GuestAccessDto
{
    public Guid TableSessionId { get; set; }
    public DateTime ExpiresAt { get; set; }
}
