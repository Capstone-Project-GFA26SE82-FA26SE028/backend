using Catalog.Common.DTOs;
using Shared.Common.DTOs;

namespace Catalog.BLL.Implements;

public sealed partial class RecipeService
{
    private async Task<ResponseDTO?> ValidateMediaReferences(CreateRecipeVersionDTO request, Guid? itemId,
        Guid? optionId, CancellationToken ct)
    {
        if (request.Media is null)
        {
            return null;
        }

        foreach (var media in request.Media)
        {
            if (media.MediaType == "video")
            {
                if (RecipeMediaRules.IsVideoLink(media.Url))
                {
                    continue;
                }

                return new ResponseDTO("Link video phải là URL HTTP/HTTPS hợp lệ, không chứa thông tin đăng nhập.", 400);
            }

            if (RecipeMediaRules.IsCloudUrl(media.Url, media.MediaType, cloudinary.CloudName))
            {
                continue;
            }

            // Historical media can only be carried forward within the same recipe owner.
            if (Uri.TryCreate(media.Url, UriKind.Absolute, out var uri)
                && uri.Scheme is "http" or "https"
                && await recipes.HasMediaAsync(itemId, optionId, media.MediaType!, media.Url!, ct))
            {
                continue;
            }

            return new ResponseDTO("Ảnh mới phải tải lên Cloudinary; link video phải là URL HTTP/HTTPS hợp lệ.", 400);
        }

        return null;
    }
}
