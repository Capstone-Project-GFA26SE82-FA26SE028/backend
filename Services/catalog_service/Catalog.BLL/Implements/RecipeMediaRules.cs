using System.Text.RegularExpressions;

namespace Catalog.BLL.Implements;

public static class RecipeMediaRules
{
    public static bool IsVideoLink(string? url)
    {
        return !string.IsNullOrWhiteSpace(url) && url.Length <= 2048 && !url.Any(char.IsControl)
            && Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri)
            && uri.Scheme is "http" or "https" && uri.Host.Length > 0 && uri.UserInfo.Length == 0;
    }

    public static string? DetectType(byte[] file)
    {
        ReadOnlySpan<byte> bytes = file;
        if (bytes.StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })
            || bytes.StartsWith(new byte[] { 255, 216, 255 })
            || (bytes.Length >= 12 && bytes[..4].SequenceEqual("RIFF"u8) && bytes.Slice(8, 4).SequenceEqual("WEBP"u8)))
        {
            return "image";
        }

        if ((bytes.Length >= 12 && bytes.Slice(4, 4).SequenceEqual("ftyp"u8))
            || bytes.StartsWith(new byte[] { 26, 69, 223, 163 }))
        {
            return "video";
        }

        return null;
    }

    public static bool IsCloudUrl(string? url, string? mediaType, string cloudName)
    {
        if (string.IsNullOrWhiteSpace(cloudName) || url is null || url.Length > 2048
            || mediaType is not ("image" or "video") || !Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps || uri.Host != "res.cloudinary.com" || !uri.IsDefaultPort
            || uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0)
        {
            return false;
        }

        var cloud = Regex.Escape(Uri.EscapeDataString(cloudName));
        var folder = mediaType == "image" ? "images" : "videos";
        var formats = mediaType == "image" ? "jpg|jpeg|png|webp" : "mp4|webm";
        return Regex.IsMatch(uri.AbsolutePath,
            $"^/{cloud}/{mediaType}/upload/(?:v[0-9]+/)?catalog/recipes/{folder}/[0-9a-fA-F]{{32}}\\.(?:{formats})$",
            RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    }
}
