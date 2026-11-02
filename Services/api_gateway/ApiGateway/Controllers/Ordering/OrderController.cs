using ApiGateway.DTOs.Ordering;
using ApiGateway.Settings;
using ApiGateway.Swagger;
using Backend.Contracts.Ordering;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Shared.Common.DTOs;

namespace ApiGateway.Controllers.Ordering;

[ApiController]
[Route("api/table-sessions/{sessionId:guid}/orders")]
[RequireBearerToken(Optional = true)]
public sealed class OrderController(OrderGrpc.OrderGrpcClient client, IOptions<OrderingClientSettings> settings)
    : OrderingControllerBase(settings)
{
    [HttpPost]
    public Task<IActionResult> Create(Guid sessionId, CreateOrderDTO body,
        [FromHeader(Name = "Idempotency-Key")] string key, CancellationToken ct)
    {
        return Execute(async () =>
        {
            if (!ValidLines(body.Items))
            {
                return BadRequest(new ResponseDTO("Các món trong đơn hàng không hợp lệ.", 400));
            }

            var request = new CreateOrderRequest
            {
                Access = Access(sessionId),
                IdempotencyKey = key
            };
            if (body.CartId.HasValue)
            {
                request.CartId = body.CartId.Value.ToString();
            }

            if (body.CartVersion is not null)
            {
                request.CartVersion = body.CartVersion;
            }

            if (body.TableId.HasValue)
            {
                request.TableId = body.TableId.Value.ToString();
            }

            if (!string.IsNullOrWhiteSpace(body.Channel))
            {
                request.Channel = body.Channel;
            }

            request.Items.AddRange(body.Items.Select(OrderingMapping.WriteSelection));
            var reply = await client.CreateAsync(request, Options(ct));
            if (reply.Created)
            {
                Response.Headers.Location = $"/api/table-sessions/{sessionId}/orders/{reply.Result.OrderId}";
            }

            return Result(OrderingMapping.Map(reply.Result), reply.Created ? 201 : 200);
        }, ct);
    }

    [HttpGet("{id:guid}")]
    public Task<IActionResult> GetById(Guid sessionId, Guid id, CancellationToken ct)
    {
        return Execute(async () => Result(OrderingMapping.Map(await client.GetByIdAsync(new OrderIdRequest
        {
            Access = Access(sessionId),
            OrderId = id.ToString()
        }, Options(ct)))), ct);
    }

    [HttpGet]
    public Task<IActionResult> GetAll(Guid sessionId, CancellationToken ct, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        return Execute(async () =>
        {
            var reply = await client.GetAllAsync(new OrderListRequest
            {
                Access = Access(sessionId),
                Page = page,
                PageSize = pageSize
            }, Options(ct));
            return Result(reply.Items.Select(OrderingMapping.Map).ToList());
        }, ct);
    }

    [HttpPost("{id:guid}/confirm")]
    [RequireBearerToken]
    public Task<IActionResult> Confirm(Guid sessionId, Guid id, ConfirmOrderDTO body, CancellationToken ct)
    {
        return Execute(async () => Result(OrderingMapping.Map(await client.ConfirmAsync(new OrderIdRequest
        {
            Access = Access(sessionId),
            OrderId = id.ToString(),
            Version = body.Version ?? string.Empty
        }, Options(ct)))), ct);
    }

    [HttpPost("{id:guid}/items/{orderItemId:guid}/serve")]
    [RequireBearerToken]
    public Task<IActionResult> ServeItem(Guid sessionId, Guid id, Guid orderItemId, ServeOrderItemDTO body, CancellationToken ct)
    {
        return Execute(async () =>
        {
            var request = new ServeOrderItemRequest
            {
                Access = Access(sessionId),
                OrderId = id.ToString(),
                OrderItemId = orderItemId.ToString()
            };

            if (!string.IsNullOrEmpty(body.Version))
            {
                request.Version = body.Version;
            }

            var reply = await client.ServeItemAsync(request, Options(ct));
            return Result(OrderingMapping.Map(reply));
        }, ct);
    }
}
