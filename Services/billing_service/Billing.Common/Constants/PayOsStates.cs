namespace Billing.Common.Constants;

public static class PayOsStates
{
    public const string Method = "BankTransfer";
    public const string KeyPrefix = "PayOS:";
    public const string Provider = "PayOS";
    public const string Creating = "Creating";
    public const string Pending = "Pending";
    public const string Unknown = "Unknown";
    public const string CancelRequested = "CancelRequested";
    public const string ReviewRequired = "ReviewRequired";
    public const string QueryVerified = "PayOSQuery";
    public const string WebhookVerified = "PayOSWebhook";
    public const string ZeroVerified = "PayOSZero";
    public static readonly string[] ZeroStatuses = ["Cancelled", "Expired", "Failed"];
}
