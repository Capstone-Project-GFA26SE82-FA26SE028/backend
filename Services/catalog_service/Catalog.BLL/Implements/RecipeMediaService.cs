using Catalog.BLL.Interfaces;
using Catalog.Common.DTOs;
using Microsoft.Extensions.Options;
using Shared.Common.Images;

namespace Catalog.BLL.Implements;

public sealed class RecipeMediaService(ICatalogAccess access, IRecipeMediaStorage storage,
    IOptions<RecipeMediaSettings> settings) : IRecipeMediaService
{
    public async Task<RecipeMediaUploadDTO> Upload(byte[] file, CancellationToken ct)
    {
        await access.RequireMenuManagerAsync(ct);
        var type = RecipeMediaRules.DetectType(file);
        if (type is null)
        {
            throw new ImageStorageException(400, "Chọn ảnh JPEG/PNG/WebP hoặc video MP4/WebM hợp lệ.");
        }

        var maximum = type == "image" ? settings.Value.MaximumImageBytes : settings.Value.MaximumVideoBytes;
        if (file.Length == 0 || file.Length > maximum)
        {
            throw new ImageStorageException(400, $"File vượt giới hạn {maximum} byte cho loại {type}.");
        }

        var folder = type == "image" ? "images" : "videos";
        var url = await storage.UploadAsync(file, type, $"catalog/recipes/{folder}/{Guid.NewGuid():N}", ct);
        return new RecipeMediaUploadDTO(type, url);
    }
}
