namespace Restaurant.Common.DTOs;

public sealed class ReservationTableAvailabilityDto
{
    public Guid TableId { get; set; }
    public string TableCode { get; set; } = string.Empty;
    public int Capacity { get; set; }
    public string CleaningStatus { get; set; } = string.Empty;
    public bool IsScheduleAvailable { get; set; }
    public IReadOnlyList<string> Reasons { get; set; } = [];
}
