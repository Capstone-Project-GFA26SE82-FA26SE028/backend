using ApiGateway.DTOs.Ordering;
using ApiGateway.Settings;
using ApiGateway.Swagger;
using Backend.Contracts.Ordering;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Shared.Common.DTOs;

namespace ApiGateway.Controllers.Ordering;

[ApiController]
[Route("api/restaurants/{restaurantId:guid}/takeaway-cart")]
[RequireBearerToken]
public sealed class TakeawayCartController(CartGrpc.CartGrpcClient client, IOptions<OrderingClientSettings> settings)
    : OrderingControllerBase(settings)
{
    [HttpPost]
    public Task<IActionResult> Create(Guid restaurantId, CancellationToken ct)
    {
        return Call(restaurantId, null, "Create", null, ct);
    }

    [HttpGet("{id:guid}")]
    public Task<IActionResult> GetById(Guid restaurantId, Guid id, CancellationToken ct)
    {
        return Call(restaurantId, id, "Get", null, ct);
    }

    [HttpPut("{id:guid}")]
    public Task<IActionResult> Update(Guid restaurantId, Guid id, CartWriteDTO body, CancellationToken ct)
    {
        return Call(restaurantId, id, "Update", body, ct);
    }

    [HttpDelete("{id:guid}")]
    public Task<IActionResult> Delete(Guid restaurantId, Guid id, [FromQuery] string version, CancellationToken ct)
    {
        return Call(restaurantId, id, "Delete", new CartWriteDTO
        {
            Version = version
        }, ct);
    }

    private Task<IActionResult> Call(Guid restaurantId, Guid? id, string operation, CartWriteDTO? body, CancellationToken ct)
    {
        return Execute(async () =>
        {
            if (body is not null && !ValidLines(body.Items))
            {
                return BadRequest(new ResponseDTO("Danh sách món trong giỏ hàng không hợp lệ.", 400));
            }

            var request = new TakeawayCartRequest
            {
                RestaurantId = restaurantId.ToString(),
                Operation = operation,
                Version = body?.Version ?? ""
            };
            if (id.HasValue)
            {
                request.CartId = id.Value.ToString();
            }

            if (body is not null)
            {
                request.Items.AddRange(body.Items.Select(OrderingMapping.WriteSelection));
            }

            return Result(OrderingMapping.Map(await client.TakeawayAsync(request, Options(ct))));
        }, ct);
    }
}
