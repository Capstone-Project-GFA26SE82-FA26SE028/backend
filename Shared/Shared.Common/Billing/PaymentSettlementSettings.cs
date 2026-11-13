namespace Shared.Common.Billing;

public sealed class PaymentSettlementSettings
{
    public string SharedKey { get; set; } = string.Empty;
    public int TimeoutSeconds { get; set; } = 5;
    public int RetryIntervalSeconds { get; set; } = 5;
    public int BatchSize { get; set; } = 20;
}
