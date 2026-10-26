using Restaurant.BLL.Interfaces;
using Restaurant.Common.Exceptions;
using Shared.Common.Images;

namespace Restaurant.BLL.Implements;

public sealed class QrImageStore(IImageStorage storage) : IQrImageStore
{
    public async Task<string> SaveAsync(Guid id, byte[] png, CancellationToken ct)
    {
        try
        {
            return await storage.UploadAsync(png, $"restaurant/qr/{id:N}", ct);
        }
        catch (ImageStorageException ex)
        {
            throw new ManagementException(ex.StatusCode, ex.Message);
        }
    }

    public async Task<byte[]> ReadAsync(string url, CancellationToken ct)
    {
        try
        {
            return await storage.ReadAsync(url, ct);
        }
        catch (ImageStorageException ex)
        {
            throw new ManagementException(ex.StatusCode, ex.Message);
        }
    }
}
