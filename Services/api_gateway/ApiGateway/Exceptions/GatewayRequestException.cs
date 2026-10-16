namespace ApiGateway.Exceptions;

public sealed class GatewayRequestException(int statusCode, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}
