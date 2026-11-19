using ApiGateway.DTOs.Ordering;
using ApiGateway.Settings;
using ApiGateway.Swagger;
using Backend.Contracts.Ordering;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace ApiGateway.Controllers.Ordering;

[ApiController]
[Route("api/table-sessions/{sessionId:guid}/cancellation-requests")]
[RequireBearerToken(Optional = true)]
public sealed class CancellationRequestController(CancellationRequestGrpc.CancellationRequestGrpcClient client,
    IOptions<OrderingClientSettings> settings) : OrderingControllerBase(settings)
{
    [HttpPost]
    public Task<IActionResult> Create(Guid sessionId, CreateCancellationRequestDTO body,
        [FromHeader(Name = "Idempotency-Key")] Guid key, CancellationToken ct)
    {
        return Execute(async () =>
        {
            var reply = await client.CreateAsync(new CancellationWriteRequest
            {
                Access = Access(sessionId),
                IdempotencyKey = key.ToString(),
                OrderItemId = body.OrderItemId.ToString(),
                Quantity = body.Quantity,
                Reason = body.Reason ?? ""
            }, Options(ct));
            return Result(Map(reply.Result), reply.Created ? 201 : 200);
        }, ct);
    }

    [HttpGet]
    public Task<IActionResult> GetAll(Guid sessionId, CancellationToken ct, string? status = null, int page = 1, int pageSize = 20)
    {
        return List(Access(sessionId), status, page, pageSize, ct);
    }

    [HttpGet("/api/cancellation-requests")]
    [RequireBearerToken]
    public Task<IActionResult> GetForRestaurant(CancellationToken ct, string? status = null, int page = 1, int pageSize = 20)
    {
        return List(new OrderingAccess(), status, page, pageSize, ct);
    }

    [HttpGet("{id:guid}")]
    public Task<IActionResult> GetById(Guid sessionId, Guid id, CancellationToken ct)
    {
        return Execute(async () =>
        {
            return Result(Map(await client.GetByIdAsync(new CancellationDecisionRequest
            {
                Access = Access(sessionId),
                Id = id.ToString()
            }, Options(ct))));
        }, ct);
    }

    [HttpPost("{id:guid}/approve")]
    [RequireBearerToken]
    public Task<IActionResult> Approve(Guid sessionId, Guid id, CancellationToken ct)
    {
        return Execute(async () =>
        {
            return Result(Map(await client.ApproveAsync(new CancellationDecisionRequest
            {
                Access = Access(sessionId),
                Id = id.ToString()
            }, Options(ct))));
        }, ct);
    }

    [HttpPost("{id:guid}/reject")]
    [RequireBearerToken]
    public Task<IActionResult> Reject(Guid sessionId, Guid id, RejectCancellationRequestDTO body, CancellationToken ct)
    {
        return Execute(async () =>
        {
            return Result(Map(await client.RejectAsync(new CancellationDecisionRequest
            {
                Access = Access(sessionId),
                Id = id.ToString(),
                Reason = body.Reason ?? ""
            }, Options(ct))));
        }, ct);
    }

    private Task<IActionResult> List(OrderingAccess access, string? status, int page, int pageSize, CancellationToken ct)
    {
        return Execute(async () =>
        {
            var reply = await client.GetAllAsync(new CancellationListRequest
            {
                Access = access,
                Status = status ?? "",
                Page = page,
                PageSize = pageSize
            }, Options(ct));
            return Result(new
            {
                Items = reply.Items.Select(Map).ToArray(),
                reply.TotalCount,
                reply.Page,
                reply.PageSize
            });
        }, ct);
    }

    private static object Map(CancellationInfo value)
    {
        return new
        {
            value.CancellationRequestId,
            value.TableSessionId,
            value.OrderId,
            value.OrderNumber,
            value.OrderItemId,
            value.ItemName,
            value.Quantity,
            value.Reason,
            value.RequestedByRef,
            DecidedByRef = value.HasDecidedByRef ? value.DecidedByRef : null,
            value.Status,
            value.RequestedAt,
            DecidedAt = value.HasDecidedAt ? value.DecidedAt : null,
            DecisionReason = value.HasDecisionReason ? value.DecisionReason : null
        };
    }
}
