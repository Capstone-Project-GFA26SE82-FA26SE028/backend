namespace Restaurant.Common.Settings;

public sealed class ReservationSettings
{
    public string[] StaffRoleIds { get; set; } = [];
    public int DefaultDurationMinutes { get; set; }
    public Dictionary<Guid, int> DurationMinutesByRestaurant { get; set; } = [];
    public int MinimumAdvanceMinutes { get; set; }
    public int MaximumAdvanceDays { get; set; }
    public int HoldBeforeMinutes { get; set; }
    public int HoldAfterMinutes { get; set; }
    public bool ExpirationEnabled { get; set; }
    public int ExpirationIntervalSeconds { get; set; }
    public int ExpirationBatchSize { get; set; }
}
