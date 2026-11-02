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
            throw new ImageStorageException(400, "Kích thước ảnh phải từ 1 byte đến tối đa 2 MiB.");
        }

        if (string.IsNullOrWhiteSpace(settings.CloudName)
            || string.IsNullOrWhiteSpace(settings.ApiKey)
            || string.IsNullOrWhiteSpace(settings.ApiSecret))
        {
            throw new ImageStorageException(503, "Vui lòng cấu hình Cloudinary:CloudName, ApiKey và ApiSecret.");
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
                        ? "Cloudinary từ chối hình ảnh. Vui lòng sử dụng ảnh JPEG, PNG hoặc WebP hợp lệ."
                        : "Không thể tải hình ảnh lên Cloudinary.");
            }

            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            if (!json.RootElement.TryGetProperty("secure_url", out var value)
                || value.ValueKind != JsonValueKind.String
                || !IsDeliveryUrl(value.GetString()))
            {
                throw new ImageStorageException(503, "Cloudinary trả về đường dẫn ảnh không hợp lệ.");
            }

            return value.GetString()!;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException
            || ex is OperationCanceledException && !ct.IsCancellationRequested)
        {
            throw new ImageStorageException(503, "Dịch vụ Cloudinary hiện không khả dụng. Vui lòng thử lại sau.");
        }
    }

    public async Task<byte[]> ReadAsync(string url, CancellationToken ct)
    {
        if (!IsDeliveryUrl(url))
        {
            throw new ImageStorageException(404, "Chưa có ảnh trên Cloudinary. Vui lòng tạo lại mã QR.");
        }

        try
        {
            using var response = await client.GetAsync(url, ct);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                throw new ImageStorageException(404, "Không tìm thấy hình ảnh trên Cloudinary.");
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new ImageStorageException(503, "Không thể đọc ảnh từ Cloudinary.");
            }

            return await response.Content.ReadAsByteArrayAsync(ct);
        }
        catch (Exception ex) when (ex is HttpRequestException
            || ex is OperationCanceledException && !ct.IsCancellationRequested)
        {
            throw new ImageStorageException(503, "Dịch vụ Cloudinary hiện không khả dụng. Vui lòng thử lại sau.");
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
