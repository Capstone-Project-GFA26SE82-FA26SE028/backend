namespace Billing.Common.Settings;

public sealed class BillSettings
{
    public string OrderPriceBasis { get; set; } = string.Empty;
    public string Currency { get; set; } = string.Empty;
}
