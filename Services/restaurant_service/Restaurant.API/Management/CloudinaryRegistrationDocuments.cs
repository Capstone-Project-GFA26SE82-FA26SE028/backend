using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Restaurant.BLL.Interfaces;
using Restaurant.Common.Exceptions;
using Shared.Common.Images;

namespace Restaurant.API.Management;

public sealed class CloudinaryRegistrationDocuments(HttpClient client, CloudinarySettings settings, TimeProvider clock,
    ILogger<CloudinaryRegistrationDocuments> logger)
    : IRestaurantRegistrationDocuments
{
    public async Task<string> UploadAsync(byte[] image, string publicId, CancellationToken ct)
    {
        var endpoint = Endpoint("image/upload");
        var fields = Sign(new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["allowed_formats"] = "jpg,png,webp",
            ["overwrite"] = "false",
            ["public_id"] = publicId,
            ["type"] = "authenticated"
        });
        using var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(image), "file", "document");
        foreach (var (name, value) in fields)
        {
            form.Add(new StringContent(value), name);
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = form
        };
        using var response = await SendAsync(request, "upload", ct);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("Cloudinary từ chối tải lên giấy tờ. HTTP {StatusCode}.", (int)response.StatusCode);
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
        var endpoint = Endpoint("asset/download");
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new FormUrlEncodedContent(Sign(new SortedDictionary<string, string>(StringComparer.Ordinal)
            {
                ["asset_id"] = assetId
            }))
        };
        using var response = await SendAsync(request, "download", ct);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("Cloudinary từ chối tải xuống giấy tờ. HTTP {StatusCode}.", (int)response.StatusCode);
            throw new ManagementException(response.StatusCode == HttpStatusCode.NotFound ? 404 : 503,
                "Không thể đọc ảnh giấy tờ từ Cloudinary.");
        }

        return await response.Content.ReadAsByteArrayAsync(ct);
    }

    private SortedDictionary<string, string> Sign(SortedDictionary<string, string> fields)
    {
        fields["timestamp"] = clock.GetUtcNow().ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        var signed = string.Join("&", fields.Select(field => $"{field.Key}={field.Value}")) + settings.ApiSecret;
        fields["signature"] = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(signed))).ToLowerInvariant();
        fields["api_key"] = settings.ApiKey;
        return fields;
    }

    private string Endpoint(string action)
    {
        if (string.IsNullOrWhiteSpace(settings.CloudName) || string.IsNullOrWhiteSpace(settings.ApiKey)
            || string.IsNullOrWhiteSpace(settings.ApiSecret))
        {
            logger.LogWarning("Thiếu cấu hình Cloudinary cho ảnh giấy tờ.");
            throw new ManagementException(503, "Chưa cấu hình Cloudinary cho ảnh giấy tờ.");
        }

        return $"https://api.cloudinary.com/v1_1/{Uri.EscapeDataString(settings.CloudName)}/{action}";
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, string operation, CancellationToken ct)
    {
        try
        {
            return await client.SendAsync(request, ct);
        }
        catch (Exception error) when (error is HttpRequestException or OperationCanceledException && !ct.IsCancellationRequested)
        {
            logger.LogWarning("Không thể kết nối Cloudinary khi thực hiện {Operation}. Lỗi {ErrorType}.",
                operation, error.GetType().Name);
            throw new ManagementException(503, "Cloudinary tạm thời không khả dụng.");
        }
    }
}
