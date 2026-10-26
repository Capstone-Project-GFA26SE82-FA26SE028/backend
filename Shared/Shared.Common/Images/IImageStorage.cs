namespace Shared.Common.Images;

public interface IImageStorage
{
    Task<string> UploadAsync(byte[] image, string publicId, CancellationToken ct);

    Task<byte[]> ReadAsync(string url, CancellationToken ct);
}
