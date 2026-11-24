using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Catalog.BLL.Implements;
using Catalog.BLL.Interfaces;
using Shared.Common.Images;

namespace Catalog.API.Clients;

public sealed class CloudinaryRecipeMediaStorage(HttpClient http, CloudinarySettings settings) : IRecipeMediaStorage
{
    public async Task<string> UploadAsync(byte[] file, string mediaType, string publicId, CancellationToken ct)
    {
        if (mediaType is not ("image" or "video"))
        {
            throw new ImageStorageException(400, "Loại media không hợp lệ.");
        }

        if (string.IsNullOrWhiteSpace(settings.CloudName) || string.IsNullOrWhiteSpace(settings.ApiKey)
            || string.IsNullOrWhiteSpace(settings.ApiSecret))
        {
            throw new ImageStorageException(503, "Chưa cấu hình Cloudinary cho media công thức.");
        }

        using var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(file), "file", "media");
        form.Add(new StringContent(publicId), "public_id");
        form.Add(new StringContent("false"), "overwrite");
        form.Add(new StringContent(mediaType == "image" ? "jpg,png,webp" : "mp4,webm"), "allowed_formats");
        using var request = new HttpRequestMessage(HttpMethod.Post,
            $"https://api.cloudinary.com/v1_1/{Uri.EscapeDataString(settings.CloudName)}/{mediaType}/upload");
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes($"{settings.ApiKey}:{settings.ApiSecret}")));
        request.Content = form;
        try
        {
            using var response = await http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                throw new ImageStorageException(response.StatusCode == HttpStatusCode.BadRequest ? 400 : 503,
                    response.StatusCode == HttpStatusCode.BadRequest
                        ? "Cloudinary từ chối file. Chọn ảnh JPEG/PNG/WebP hoặc video MP4/WebM hợp lệ."
                        : "Không thể tải media công thức lên Cloudinary.");
            }

            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            if (json.RootElement.ValueKind != JsonValueKind.Object
                || !json.RootElement.TryGetProperty("secure_url", out var value) || value.ValueKind != JsonValueKind.String
                || !RecipeMediaRules.IsCloudUrl(value.GetString(), mediaType, settings.CloudName))
            {
                throw new ImageStorageException(503, "Cloudinary trả về media công thức không hợp lệ.");
            }

            return value.GetString()!;
        }
        catch (Exception error) when (error is HttpRequestException or JsonException
            || error is OperationCanceledException && !ct.IsCancellationRequested)
        {
            throw new ImageStorageException(503, "Cloudinary không khả dụng hoặc đã hết thời gian tải media.");
        }
    }
}
