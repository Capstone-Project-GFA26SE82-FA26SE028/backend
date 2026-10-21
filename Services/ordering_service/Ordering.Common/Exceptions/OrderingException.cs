namespace Ordering.Common.Exceptions;

public sealed class OrderingException(int statusCode, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}
