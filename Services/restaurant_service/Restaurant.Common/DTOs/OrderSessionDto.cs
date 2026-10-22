namespace Restaurant.Common.DTOs;

public sealed class OrderSessionDto
{
    public Guid TableSessionId { get; set; }
    public Guid? TableId { get; set; }
    public Guid? UserId { get; set; }
    public Guid? GuestAccessId { get; set; }
    public DateTime? ExpiresAt { get; set; }
}
