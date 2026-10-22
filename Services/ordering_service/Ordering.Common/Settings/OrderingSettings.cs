namespace Ordering.Common.Settings;

public sealed class OrderingSettings
{
    public Guid? DefaultKitchenStationId { get; set; }
    public int DependencyTimeoutSeconds { get; set; } = 5;
}
