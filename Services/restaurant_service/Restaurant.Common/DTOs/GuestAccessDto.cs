namespace Restaurant.Common.DTOs;

public sealed class GuestAccessDto
{
    public Guid TableSessionId { get; set; }
    public DateTime ExpiresAt { get; set; }
    public string Token { get; set; } = "";
}
