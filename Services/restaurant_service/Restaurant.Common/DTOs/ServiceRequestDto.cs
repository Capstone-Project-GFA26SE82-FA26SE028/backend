namespace Restaurant.Common.DTOs;

public sealed class ServiceRequestDto
{
    public Guid ServiceRequestId { get; set; }
    public Guid TableSessionId { get; set; }
    public Guid RestaurantId { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime RequestedAt { get; set; }
    public DateTime? AcknowledgedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public Guid? HandledByRef { get; set; }
}
