namespace Restaurant.Common.Exceptions;

public sealed class ManagementException(int statusCode, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}
