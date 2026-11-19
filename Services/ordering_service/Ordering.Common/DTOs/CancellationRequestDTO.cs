namespace Ordering.Common.DTOs;

public sealed class CancellationRequestDTO
{
    public Guid CancellationRequestId { get; set; }
    public Guid TableSessionId { get; set; }
    public Guid OrderId { get; set; }
    public string OrderNumber { get; set; } = "";
    public Guid OrderItemId { get; set; }
    public string ItemName { get; set; } = "";
    public int Quantity { get; set; }
    public string Reason { get; set; } = "";
    public Guid RequestedByRef { get; set; }
    public Guid? DecidedByRef { get; set; }
    public string Status { get; set; } = "";
    public DateTime RequestedAt { get; set; }
    public DateTime? DecidedAt { get; set; }
    public string? DecisionReason { get; set; }
}
