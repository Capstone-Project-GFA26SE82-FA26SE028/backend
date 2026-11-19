using ApiGateway.Swagger;
using Backend.Contracts.Identity;
using Microsoft.AspNetCore.Mvc;
using Shared.Common.DTOs;

namespace ApiGateway.Controllers.IdentityController;

[ApiController]
[RequireBearerToken]
public sealed class StaffTransferController(StaffTransferGrpc.StaffTransferGrpcClient client) : IdentityControllerBase
{
    [HttpPost("api/users/{userId:guid}/restaurant-transfer")]
    public Task<IActionResult> Create(Guid userId, TransferRestaurantBody body,
        [FromHeader(Name = "Idempotency-Key")] string? key, CancellationToken ct)
    {
        return Execute(async () =>
        {
            if (!Guid.TryParse(key, out var id) || id == Guid.Empty || body.RestaurantId == Guid.Empty)
            {
                return BadRequest(new ResponseDTO("Cần Idempotency-Key UUID và restaurantId hợp lệ.", 400));
            }

            return Map(await client.CreateAsync(new CreateStaffTransferRequest
            {
                TransferId = id.ToString(),
                UserId = userId.ToString(),
                TargetRestaurantId = body.RestaurantId.ToString()
            }, Options(ct)));
        }, ct);
    }

    [HttpGet("api/staff-transfers/{id:guid}")]
    public Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        return Execute(async () => Map(await client.GetByIdAsync(new StaffTransferIdRequest
        {
            TransferId = id.ToString()
        }, Options(ct))), ct);
    }

    private IActionResult Map(StaffTransferReply reply)
    {
        var item = reply.Result;
        return StatusCode(reply.StatusCode, new ResponseDTO(reply.Message, reply.StatusCode, reply.IsSuccess,
            item is null ? null : new
            {
                item.Id,
                item.UserId,
                item.SourceRestaurantId,
                item.TargetRestaurantId,
                item.Status,
                item.Message,
                CreatedAt = item.CreatedAt?.ToDateTime(),
                CompletedAt = item.CompletedAt?.ToDateTime()
            }));
    }
}

public sealed class TransferRestaurantBody
{
    public Guid RestaurantId { get; set; }
}
