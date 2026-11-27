namespace Billing.Common.Exceptions;

public sealed class BillingException(int statusCode, string message, string? code = null, decimal? amount = null) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
    public string? Code { get; } = code;
    public decimal? Amount { get; } = amount;
}
