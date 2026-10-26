using Shared.Common.Images;

namespace Catalog.Tests;

internal sealed class TestImageStorage : IImageStorage
{
    public bool FailUpload { get; set; }
    public int UploadCount { get; private set; }

    public Task<string> UploadAsync(byte[] image, string publicId, CancellationToken ct)
    {
        UploadCount++;
        if (FailUpload)
        {
            throw new ImageStorageException(503, "Cloudinary is unavailable.");
        }

        return Task.FromResult($"https://res.cloudinary.com/test/image/upload/{publicId}.png");
    }

    public Task<byte[]> ReadAsync(string url, CancellationToken ct)
    {
        throw new NotSupportedException();
    }
}
