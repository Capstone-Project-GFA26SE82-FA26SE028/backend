using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Restaurant.BLL.Interfaces;
using Restaurant.Common.Exceptions;
using Shared.Common.Images;

namespace Restaurant.API.Management;

public sealed class CloudinaryRegistrationDocuments(HttpClient client, CloudinarySettings settings, TimeProvider clock)
    : IRestaurantRegistrationDocuments
{
    public async Task<string> UploadAsync(byte[] image, string publicId, CancellationToken ct)
    {
        using var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(image), "file", "document");
        form.Add(new StringContent(publicId), "public_id");
        form.Add(new StringContent("false"), "overwrite");
        form.Add(new StringContent("jpg,png,webp"), "allowed_formats");
        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint("image/authenticated"))
        {
            Content = form
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes($"{settings.ApiKey}:{settings.ApiSecret}")));
        using var response = await SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new ManagementException(response.StatusCode == HttpStatusCode.BadRequest ? 400 : 503,
                "Cloudinary không thể lưu ảnh giấy tờ.");
        }

        try
        {
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            var data = json.RootElement;
            if (data.TryGetProperty("type", out var type) && type.GetString() == "authenticated"
                && data.TryGetProperty("asset_id", out var value) && value.GetString() is { Length: > 0 and <= 100 } assetId)
            {
                return assetId;
            }
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException)
        {
            throw new ManagementException(503, "Phản hồi lưu giấy tờ từ Cloudinary không hợp lệ.");
        }

        throw new ManagementException(503, "Cloudinary chưa xác nhận ảnh giấy tờ được bảo vệ.");
    }

    public async Task<byte[]> ReadAsync(string assetId, CancellationToken ct)
    {
        var timestamp = clock.GetUtcNow().ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        var signed = $"asset_id={assetId}&timestamp={timestamp}{settings.ApiSecret}";
        var signature = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(signed))).ToLowerInvariant();
        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint("asset/download"))
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["asset_id"] = assetId,
                ["timestamp"] = timestamp,
                ["api_key"] = settings.ApiKey,
                ["signature"] = signature
            })
        };
        using var response = await SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new ManagementException(response.StatusCode == HttpStatusCode.NotFound ? 404 : 503,
                "Không thể đọc ảnh giấy tờ từ Cloudinary.");
        }

        return await response.Content.ReadAsByteArrayAsync(ct);
    }

    private string Endpoint(string action)
    {
        if (string.IsNullOrWhiteSpace(settings.CloudName) || string.IsNullOrWhiteSpace(settings.ApiKey)
            || string.IsNullOrWhiteSpace(settings.ApiSecret))
        {
            throw new ManagementException(503, "Chưa cấu hình Cloudinary cho ảnh giấy tờ.");
        }

        return $"https://api.cloudinary.com/v1_1/{Uri.EscapeDataString(settings.CloudName)}/{action}";
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        try
        {
            return await client.SendAsync(request, ct);
        }
        catch (Exception error) when (error is HttpRequestException or OperationCanceledException && !ct.IsCancellationRequested)
        {
            throw new ManagementException(503, "Cloudinary tạm thời không khả dụng.");
        }
    }
}
