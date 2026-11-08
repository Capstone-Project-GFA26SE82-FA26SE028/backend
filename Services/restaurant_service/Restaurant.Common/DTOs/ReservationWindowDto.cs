namespace Restaurant.Common.DTOs;

public sealed class ReservationWindowDto
{
    public DateTime StartAt { get; set; }
    public DateTime EndAt { get; set; }
    public DateTime HoldStartsAt { get; set; }
    public DateTime HoldUntil { get; set; }
    public DateTime BlockedUntil { get; set; }
}
