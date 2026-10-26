using Catalog.BLL.Interfaces;
using Shared.Common.Images;

namespace Catalog.BLL.Implements;

public sealed class ItemImageService(ICatalogAccess access, IImageStorage storage) : IItemImageService
{
    public async Task<string> Upload(byte[] image, CancellationToken ct)
    {
        await access.RequireMenuManagerAsync(ct);
        return await storage.UploadAsync(image, $"catalog/items/{Guid.NewGuid():N}", ct);
    }
}
