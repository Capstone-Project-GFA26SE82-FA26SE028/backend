namespace Billing.Common.Settings;

public sealed class CashRegisterAccessSettings
{
    public string[] ManagerRoleIds { get; set; } = [];
    public string[] CashierRoleIds { get; set; } = [];
}
