using Catalog.BLL.Interfaces;
using Shared.Common.Images;

namespace Catalog.BLL.Implements;

public sealed class ItemImageService(ICatalogAccess access, IImageStorage storage) : IItemImageService
{
    public async Task<string> Upload(byte[] image, CancellationToken ct)
    {
        await access.RequireMenuManagerAsync(ct);
        ReadOnlySpan<byte> bytes = image;
        var validFormat = bytes.StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })
            || bytes.StartsWith(new byte[] { 255, 216, 255 })
            || (bytes.Length >= 12 && bytes[..4].SequenceEqual("RIFF"u8) && bytes.Slice(8, 4).SequenceEqual("WEBP"u8));
        if (image.Length == 0 || image.Length > CloudinaryImageStorage.MaxImageBytes || !validFormat)
        {
            throw new ImageStorageException(400, "Chọn ảnh JPEG, PNG hoặc WebP hợp lệ, tối đa 2 MiB.");
        }

        return await storage.UploadAsync(image, $"catalog/items/{Guid.NewGuid():N}", ct);
    }
}
