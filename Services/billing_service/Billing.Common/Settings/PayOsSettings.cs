namespace Billing.Common.Settings;

public sealed class PayOsSettings
{
    public bool Enabled { get; set; }
    public string BaseUrl { get; set; } = "https://api-merchant.payos.vn";
    public string ClientId { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public string ChecksumKey { get; set; } = string.Empty;
    public string ReturnUrl { get; set; } = string.Empty;
    public string CancelUrl { get; set; } = string.Empty;
    public string WebhookUrl { get; set; } = string.Empty;
    public int LinkLifetimeMinutes { get; set; } = 15;
    public int RequestTimeoutSeconds { get; set; } = 10;
    public int ReconcileIntervalSeconds { get; set; } = 30;
    public int ReconcileBatchSize { get; set; } = 20;
    public int PaidReconcileHours { get; set; } = 24;
    public bool AllowInsecureTestTransport { get; set; }
}
