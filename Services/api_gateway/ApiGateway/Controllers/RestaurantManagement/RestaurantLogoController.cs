using ApiGateway.Swagger;
using Backend.Contracts.Restaurant;
using Google.Protobuf;
using Microsoft.AspNetCore.Mvc;
using Shared.Common.DTOs;
using Shared.Common.Images;

namespace ApiGateway.Controllers.RestaurantManagement;

[ApiController]
[Route("api/restaurants/{id:guid}/logo")]
[RequireBearerToken]
public sealed class RestaurantLogoController(RestaurantGrpc.RestaurantGrpcClient client) : ManagementControllerBase
{
    [HttpPost]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(CloudinaryImageStorage.MaxImageBytes + 65536)]
    [RequestFormLimits(MultipartBodyLengthLimit = CloudinaryImageStorage.MaxImageBytes + 65536)]
    public Task<IActionResult> Upload(Guid id, IFormFile file, CancellationToken ct)
    {
        return Execute(async () =>
        {
            if (file.Length == 0 || file.Length > CloudinaryImageStorage.MaxImageBytes)
            {
                return BadRequest(new ResponseDTO("Chọn logo JPEG, PNG hoặc WebP hợp lệ, tối đa 2 MiB.", 400));
            }

            using var stream = file.OpenReadStream();
            var reply = await client.UploadLogoAsync(new RestaurantLogoUploadRequest
            {
                RestaurantId = id.ToString(),
                Image = await ByteString.FromStreamAsync(stream, ct)
            }, Options(ct));
            return new
            {
                RestaurantId = id,
                reply.LogoUrl
            };
        }, ct);
    }
}
