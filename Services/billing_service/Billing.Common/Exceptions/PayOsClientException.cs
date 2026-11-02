namespace Billing.Common.Exceptions;

public sealed class PayOsClientException(string failure, string? providerCode = null) : Exception("Không thể xác minh chắc chắn kết quả yêu cầu PayOS.")
{
    public string Failure { get; } = failure;
    public string? ProviderCode { get; } = providerCode;
}
