using ApiGateway.Swagger;
using Backend.Contracts.Identity;
using Google.Protobuf;
using Microsoft.AspNetCore.Mvc;
using Shared.Common.DTOs;
using Shared.Common.Images;

namespace ApiGateway.Controllers.IdentityController;

[ApiController]
[Route("api/users")]
[RequireBearerToken]
public sealed class UserAvatarController(UserGrpc.UserGrpcClient client) : IdentityControllerBase
{
    [HttpPost("me/avatar")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(CloudinaryImageStorage.MaxImageBytes + 65536)]
    [RequestFormLimits(MultipartBodyLengthLimit = CloudinaryImageStorage.MaxImageBytes + 65536)]
    public Task<IActionResult> UploadMe(IFormFile file, CancellationToken ct)
    {
        return Upload(null, file, ct);
    }

    [HttpPost("{id:guid}/avatar")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(CloudinaryImageStorage.MaxImageBytes + 65536)]
    [RequestFormLimits(MultipartBodyLengthLimit = CloudinaryImageStorage.MaxImageBytes + 65536)]
    public Task<IActionResult> UploadForUser(Guid id, IFormFile file, CancellationToken ct)
    {
        return Upload(id, file, ct);
    }

    private Task<IActionResult> Upload(Guid? id, IFormFile file, CancellationToken ct)
    {
        return Execute(async () =>
        {
            if (file.Length == 0 || file.Length > CloudinaryImageStorage.MaxImageBytes)
            {
                return BadRequest(new ResponseDTO("Chọn ảnh JPEG, PNG hoặc WebP hợp lệ, tối đa 2 MiB.", 400));
            }

            using var stream = file.OpenReadStream();
            var request = new UploadAvatarRequest
            {
                Image = await ByteString.FromStreamAsync(stream, ct)
            };
            if (id.HasValue)
            {
                request.UserId = id.Value.ToString();
            }

            var reply = await client.UploadAvatarAsync(request, Options(ct));
            return StatusCode(reply.StatusCode, new ResponseDTO
            {
                StatusCode = reply.StatusCode,
                IsSuccess = reply.IsSuccess,
                Message = reply.Message,
                Result = reply.Result is null ? null : new
                {
                    reply.Result.UserId,
                    AvatarUrl = reply.Result.HasAvatarUrl ? reply.Result.AvatarUrl : null
                }
            });
        }, ct);
    }
}
