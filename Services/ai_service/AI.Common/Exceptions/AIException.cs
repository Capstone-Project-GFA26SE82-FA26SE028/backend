namespace AI.Common.Exceptions;

public sealed class AIException(int statusCode, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}
