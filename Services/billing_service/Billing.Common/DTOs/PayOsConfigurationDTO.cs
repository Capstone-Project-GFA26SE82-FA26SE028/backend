namespace Billing.Common.DTOs;

public sealed class SavePayOsConfigurationDTO
{
    public string ClientId { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public string ChecksumKey { get; set; } = string.Empty;
    public string ReturnUrl { get; set; } = string.Empty;
    public string CancelUrl { get; set; } = string.Empty;
    public bool Enabled { get; set; }
}

public sealed class PayOsConfigurationDTO
{
    public Guid RestaurantId { get; set; }
    public bool Configured { get; set; }
    public string ClientId { get; set; } = string.Empty;
    public string ReturnUrl { get; set; } = string.Empty;
    public string CancelUrl { get; set; } = string.Empty;
    public string WebhookUrl { get; set; } = string.Empty;
    public bool Enabled { get; set; }
}

public sealed class PayOsCredentials
{
    public string ApiKey { get; set; } = string.Empty;
    public string ChecksumKey { get; set; } = string.Empty;
}
