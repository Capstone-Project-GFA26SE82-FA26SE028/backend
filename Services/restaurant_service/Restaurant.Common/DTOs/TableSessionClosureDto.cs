namespace Restaurant.Common.DTOs;

public sealed class TableSessionClosureDto
{
    public Guid TableSessionId { get; set; }
    public Guid RestaurantId { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset ClosedAt { get; set; }
    public Guid[] TableIds { get; set; } = [];
}
