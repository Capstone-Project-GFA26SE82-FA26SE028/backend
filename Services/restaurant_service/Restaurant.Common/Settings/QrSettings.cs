namespace Restaurant.Common.Settings;

public sealed class QrSettings
{
    public string ImageDirectory { get; set; } = "App_Data/qr-images";
    public string MenuBaseUrl { get; set; } = "";
}
