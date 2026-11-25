namespace ApiGateway.DTOs.Restaurant;

public sealed class GuestAccessDto
{
    public Guid TableSessionId { get; set; }
    public DateTime ExpiresAt { get; set; }
    public Guid? CustomerId { get; set; }
    public string? CustomerName { get; set; }
    public int? AvailablePoints { get; set; }
    public string? LoyaltyTierName { get; set; }
}
