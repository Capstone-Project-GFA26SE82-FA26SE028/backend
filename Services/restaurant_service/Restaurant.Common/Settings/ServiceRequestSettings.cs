namespace Restaurant.Common.Settings;

public sealed class ServiceRequestSettings
{
    public string[] WaiterRoleIds { get; set; } = [];
    public string[] CashierRoleIds { get; set; } = [];
    public int DependencyTimeoutSeconds { get; set; } = 10;
}
