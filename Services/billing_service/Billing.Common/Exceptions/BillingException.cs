namespace Billing.Common.Exceptions;

public sealed class BillingException(int statusCode, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}
