using ApiGateway.Settings;
using ApiGateway.Swagger;
using Backend.Contracts.Catalog;
using Google.Protobuf;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Shared.Common.DTOs;
using Shared.Common.Images;

namespace ApiGateway.Controllers.CatalogController;

[ApiController]
[Route("api/recipes/media")]
[RequireBearerToken]
public sealed class RecipeMediaController(RecipeMediaGrpc.RecipeMediaGrpcClient client, IOptions<CatalogClientSettings> settings,
    IOptions<RecipeMediaSettings> mediaSettings) : CatalogControllerBase(settings)
{
    [HttpPost]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(RecipeMediaSettings.TransportLimitBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = RecipeMediaSettings.TransportLimitBytes)]
    public Task<IActionResult> Upload(IFormFile file, CancellationToken ct)
    {
        return Execute(async () =>
        {
            if (file.Length == 0 || file.Length > Math.Max(mediaSettings.Value.MaximumVideoBytes, mediaSettings.Value.MaximumImageBytes))
            {
                return BadRequest(new ResponseDTO("File media công thức rỗng hoặc vượt giới hạn upload.", 400));
            }

            using var stream = file.OpenReadStream();
            var options = Options(ct).WithDeadline(DateTime.UtcNow.AddSeconds(mediaSettings.Value.UploadTimeoutSeconds + 10));
            var result = await client.UploadAsync(new RecipeMediaUploadRequest
            {
                File = await ByteString.FromStreamAsync(stream, ct)
            }, options);
            return Ok(new ResponseDTO("Tải media công thức thành công.", 200, true, new
            {
                result.MediaType,
                result.Url
            }));
        }, ct);
    }
}
