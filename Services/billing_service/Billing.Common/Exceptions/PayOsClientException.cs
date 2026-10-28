namespace Billing.Common.Exceptions;

public sealed class PayOsClientException(string failure, string? providerCode = null) : Exception("PayOS request could not be conclusively verified.")
{
    public string Failure { get; } = failure;
    public string? ProviderCode { get; } = providerCode;
}
