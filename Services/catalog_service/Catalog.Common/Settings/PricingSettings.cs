namespace Catalog.Common.Settings;

public sealed class PricingSettings
{
    public string AllChannelsCode { get; set; } = string.Empty;
    public string[] Channels { get; set; } = [];
    public string BusinessTimeZone { get; set; } = string.Empty;
}
