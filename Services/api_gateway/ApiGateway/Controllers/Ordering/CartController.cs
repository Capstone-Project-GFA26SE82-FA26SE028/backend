using ApiGateway.DTOs.Ordering;
using ApiGateway.Settings;
using ApiGateway.Swagger;
using Backend.Contracts.Ordering;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Shared.Common.DTOs;

namespace ApiGateway.Controllers.Ordering;

[ApiController]
[Route("api/table-sessions/{sessionId:guid}/carts")]
[RequireBearerToken(Optional = true)]
public sealed class CartController(CartGrpc.CartGrpcClient client, IOptions<OrderingClientSettings> settings)
    : OrderingControllerBase(settings)
{
    [HttpPost]
    public Task<IActionResult> Create(Guid sessionId, CancellationToken ct)
    {
        return Execute(async () =>
        {
            var reply = await client.CreateAsync(new CartCreateRequest
            {
                Access = Access(sessionId)
            }, Options(ct));
            return Result(OrderingMapping.Map(reply));
        }, ct);
    }

    [HttpGet("{id:guid}")]
    public Task<IActionResult> GetById(Guid sessionId, Guid id, CancellationToken ct)
    {
        return Execute(async () =>
        {
            var reply = await client.GetByIdAsync(new CartIdRequest
            {
                Access = Access(sessionId),
                CartId = id.ToString()
            }, Options(ct));
            return Result(OrderingMapping.Map(reply));
        }, ct);
    }

    [HttpPut("{id:guid}")]
    public Task<IActionResult> Update(Guid sessionId, Guid id, CartWriteDTO body, CancellationToken ct)
    {
        return Execute(async () =>
        {
            if (!ValidLines(body.Items))
            {
                return BadRequest(new ResponseDTO("Các món trong giỏ hàng không hợp lệ.", 400));
            }

            var request = new CartUpdateRequest
            {
                Access = Access(sessionId),
                CartId = id.ToString(),
                Version = body.Version ?? string.Empty
            };
            request.Items.AddRange(body.Items.Select(OrderingMapping.WriteSelection));
            return Result(OrderingMapping.Map(await client.UpdateAsync(request, Options(ct))));
        }, ct);
    }

    [HttpDelete("{id:guid}")]
    public Task<IActionResult> Delete(Guid sessionId, Guid id, [FromQuery] string version, CancellationToken ct)
    {
        return Execute(async () =>
        {
            await client.DeleteAsync(new CartIdRequest
            {
                Access = Access(sessionId),
                CartId = id.ToString(),
                Version = version
            }, Options(ct));
            return Result(null);
        }, ct);
    }
}
