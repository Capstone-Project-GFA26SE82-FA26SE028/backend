namespace Restaurant.Common.DTOs;

public sealed class ReservationDto
{
    public Guid ReservationId { get; set; }
    public Guid RestaurantId { get; set; }
    public Guid RequestedAreaId { get; set; }
    public string ContactName { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public int PartySize { get; set; }
    public DateTimeOffset StartAt { get; set; }
    public DateTimeOffset EndAt { get; set; }
    public DateTimeOffset? HoldUntil { get; set; }
    public string Status { get; set; } = string.Empty;
    public Guid? ApprovedByRef { get; set; }
    public DateTimeOffset? ConfirmedAt { get; set; }
    public DateTimeOffset? CancelledAt { get; set; }
    public Guid? CancelledByRef { get; set; }
    public Guid? AssignedRestaurantId { get; set; }
    public Guid? AssignedAreaId { get; set; }
    public bool HasConsistentAssignment { get; set; }
    public Guid? TableSessionId { get; set; }
    public IReadOnlyList<Guid> AssignedTableIds { get; set; } = [];
}
