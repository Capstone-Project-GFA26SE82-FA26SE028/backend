using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Shared.Common.Images;

public sealed class CloudinaryImageStorage(HttpClient client, CloudinarySettings settings) : IImageStorage
{
    public const int MaxImageBytes = 2 * 1024 * 1024;

    public async Task<string> UploadAsync(byte[] image, string publicId, CancellationToken ct)
    {
        if (image.Length == 0 || image.Length > MaxImageBytes)
        {
            throw new ImageStorageException(400, "The image must contain between 1 byte and 2 MiB.");
        }

        if (string.IsNullOrWhiteSpace(settings.CloudName)
            || string.IsNullOrWhiteSpace(settings.ApiKey)
            || string.IsNullOrWhiteSpace(settings.ApiSecret))
        {
            throw new ImageStorageException(503, "Configure Cloudinary:CloudName, ApiKey and ApiSecret.");
        }

        using var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(image), "file", "image");
        form.Add(new StringContent(publicId), "public_id");
        form.Add(new StringContent("false"), "overwrite");
        form.Add(new StringContent("jpg,png,webp"), "allowed_formats");
        using var request = new HttpRequestMessage(HttpMethod.Post,
            $"https://api.cloudinary.com/v1_1/{Uri.EscapeDataString(settings.CloudName)}/image/upload");
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes($"{settings.ApiKey}:{settings.ApiSecret}")));
        request.Content = form;
        try
        {
            using var response = await client.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                throw new ImageStorageException(response.StatusCode == HttpStatusCode.BadRequest ? 400 : 503,
                    response.StatusCode == HttpStatusCode.BadRequest
                        ? "Cloudinary rejected the image. Use a valid JPEG, PNG or WebP image."
                        : "Unable to upload the image to Cloudinary.");
            }

            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            if (!json.RootElement.TryGetProperty("secure_url", out var value)
                || value.ValueKind != JsonValueKind.String
                || !IsDeliveryUrl(value.GetString()))
            {
                throw new ImageStorageException(503, "Cloudinary returned an invalid image URL.");
            }

            return value.GetString()!;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException
            || ex is OperationCanceledException && !ct.IsCancellationRequested)
        {
            throw new ImageStorageException(503, "Cloudinary is unavailable. Try again later.");
        }
    }

    public async Task<byte[]> ReadAsync(string url, CancellationToken ct)
    {
        if (!IsDeliveryUrl(url))
        {
            throw new ImageStorageException(404, "No Cloudinary image is available. Regenerate the QR code.");
        }

        try
        {
            using var response = await client.GetAsync(url, ct);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                throw new ImageStorageException(404, "The image was not found on Cloudinary.");
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new ImageStorageException(503, "Unable to read the Cloudinary image.");
            }

            return await response.Content.ReadAsByteArrayAsync(ct);
        }
        catch (Exception ex) when (ex is HttpRequestException
            || ex is OperationCanceledException && !ct.IsCancellationRequested)
        {
            throw new ImageStorageException(503, "Cloudinary is unavailable. Try again later.");
        }
    }

    private bool IsDeliveryUrl(string? url)
    {
        return url?.Length <= 2048
            && Uri.TryCreate(url, UriKind.Absolute, out var uri)
            && uri.Scheme == Uri.UriSchemeHttps
            && uri.Host == "res.cloudinary.com"
            && uri.IsDefaultPort
            && string.IsNullOrEmpty(uri.UserInfo)
            && uri.AbsolutePath.StartsWith($"/{Uri.EscapeDataString(settings.CloudName)}/image/upload/", StringComparison.Ordinal);
    }
}
