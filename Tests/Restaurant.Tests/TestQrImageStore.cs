using Restaurant.BLL.Interfaces;
using Restaurant.Common.Exceptions;

namespace Restaurant.Tests;

internal sealed class TestQrImageStore : IQrImageStore
{
    public Dictionary<string, byte[]> Images { get; } = new();
    public bool FailUpload { get; set; }

    public Task<string> SaveAsync(Guid id, byte[] png, CancellationToken ct)
    {
        if (FailUpload)
        {
            throw new ManagementException(503, "Cloudinary is unavailable.");
        }

        var url = $"https://res.cloudinary.com/test/image/upload/restaurant/qr/{id:N}.png";
        Images.Add(url, png);
        return Task.FromResult(url);
    }

    public Task<byte[]> ReadAsync(string url, CancellationToken ct)
    {
        if (!Images.TryGetValue(url, out var image))
        {
            throw new ManagementException(404, "Image not found.");
        }

        return Task.FromResult(image);
    }
}
