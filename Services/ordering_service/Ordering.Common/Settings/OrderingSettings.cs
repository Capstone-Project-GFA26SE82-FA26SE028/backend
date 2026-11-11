namespace Ordering.Common.Settings;

public sealed class OrderingSettings
{
    public string[] CheckoutCashierRoleIds { get; set; } = [];
    public string[] CheckoutWaiterRoleIds { get; set; } = [];

    public Guid? DefaultKitchenStationId { get; set; }
    public int DependencyTimeoutSeconds { get; set; } = 5;
}
