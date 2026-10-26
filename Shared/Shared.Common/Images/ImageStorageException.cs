namespace Shared.Common.Images;

public sealed class ImageStorageException(int statusCode, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}
