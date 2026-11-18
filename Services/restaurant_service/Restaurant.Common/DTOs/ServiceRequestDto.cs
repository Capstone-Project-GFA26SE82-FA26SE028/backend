namespace Restaurant.Common.DTOs;

public sealed class ServiceRequestDto
{
    public Guid ServiceRequestId { get; set; }
    public Guid TableSessionId { get; set; }
    public Guid RestaurantId { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime RequestedAt { get; set; }
    public DateTime? AcknowledgedAt { get; set; }
    public Guid? HandledByRef { get; set; }
    public IReadOnlyList<PaymentRequestTableDto> Tables { get; set; } = [];
}
