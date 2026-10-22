namespace Restaurant.Common.DTOs;

public sealed class ReservationAvailabilityDto
{
    public Guid RestaurantId { get; set; }
    public Guid AreaId { get; set; }
    public int PartySize { get; set; }
    public DateTimeOffset EvaluatedAt { get; set; }
    public DateTimeOffset StartAt { get; set; }
    public DateTimeOffset EndAt { get; set; }
    public DateTimeOffset HoldStartsAt { get; set; }
    public DateTimeOffset HoldUntil { get; set; }
    public DateTimeOffset BlockedUntil { get; set; }
    public int AvailableCapacity { get; set; }
    public bool HasEnoughCapacity { get; set; }
    public bool RequiresStaffLayoutReview { get; set; } = true;
    public IReadOnlyList<string> Reasons { get; set; } = [];
    public IReadOnlyList<ReservationTableAvailabilityDto> Tables { get; set; } = [];
}
