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
[Route("api/items/images")]
[RequireBearerToken]
public sealed class ItemImageController(ItemImageGrpc.ItemImageGrpcClient client,
    IOptions<CatalogClientSettings> settings) : CatalogControllerBase(settings)
{
    [HttpPost]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(CloudinaryImageStorage.MaxImageBytes + 65536)]
    [RequestFormLimits(MultipartBodyLengthLimit = CloudinaryImageStorage.MaxImageBytes)]
    public Task<IActionResult> Upload(IFormFile file, CancellationToken cancellationToken)
    {
        return Execute(async () =>
        {
            if (file.Length == 0 || file.Length > CloudinaryImageStorage.MaxImageBytes)
            {
                return BadRequest(new ResponseDTO
                {
                    StatusCode = 400,
                    IsSuccess = false,
                    Message = "Vui lòng chọn ảnh định dạng JPEG, PNG hoặc WebP có dung lượng tối đa 2 MiB."
                });
            }

            using var stream = file.OpenReadStream();
            var request = new ItemImageUploadRequest
            {
                Image = await ByteString.FromStreamAsync(stream, cancellationToken)
            };
            var reply = await client.UploadAsync(request, Options(cancellationToken));
            return Ok(new ResponseDTO
            {
                StatusCode = 200,
                IsSuccess = true,
                Message = "Tải ảnh lên thành công.",
                Result = new { ImageUrl = reply.ImageUrl }
            });
        }, cancellationToken);
    }
}
