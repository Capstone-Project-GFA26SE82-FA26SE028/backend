using System.Text.RegularExpressions;
using Shared.Common.DTOs;

namespace Catalog.BLL.Implements;

public sealed partial class ItemService
{
    private ResponseDTO? ValidateNewImageUrl(string? imageUrl)
    {
        if (imageUrl is null)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(imageSettings.CloudName))
        {
            return new ResponseDTO("Chưa cấu hình Cloudinary cho ảnh món ăn.", 503);
        }

        var cloud = Regex.Escape(Uri.EscapeDataString(imageSettings.CloudName));
        if (imageUrl.Length > 2048 || !Uri.TryCreate(imageUrl, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps || uri.Host != "res.cloudinary.com"
            || !uri.IsDefaultPort || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment)
            || !Regex.IsMatch(uri.AbsolutePath,
                $"^/{cloud}/image/upload/(?:v[0-9]+/)?catalog/items/[0-9a-fA-F]{{32}}\\.(?:jpg|jpeg|png|webp)$",
                RegexOptions.CultureInvariant | RegexOptions.NonBacktracking))
        {
            return new ResponseDTO("Ảnh món phải là URL do API tải ảnh món trả về, thuộc Cloudinary của dự án.", 400);
        }

        return null;
    }
}
