using System.Globalization;
using ApiGateway.Swagger;
using Backend.Contracts.Restaurant;
using Microsoft.AspNetCore.Mvc;
using Shared.Common.DTOs;

namespace ApiGateway.Controllers.RestaurantManagement;

[ApiController]
[Route("api/table-sessions/{sessionId:guid}/payment-requests")]
public sealed class ServiceRequestController(ServiceRequestGrpc.ServiceRequestGrpcClient client) : ManagementControllerBase
{
    [HttpPost]
    public Task<IActionResult> Create(Guid sessionId, [FromHeader(Name = "Idempotency-Key")] string? key, CancellationToken ct)
    {
        return Execute(async () =>
        {
            if (!Guid.TryParse(key, out var id) || id == Guid.Empty)
            {
                return BadRequest(new ResponseDTO("Idempotency-Key phải là UUID hợp lệ. Dùng lại khóa này khi thử lại.", 400));
            }

            return Map(await client.CreateAsync(GuestRequest(sessionId, id), Options(ct)));
        }, ct);
    }

    [HttpGet("current")]
    public Task<IActionResult> GetCurrent(Guid sessionId, CancellationToken ct)
    {
        return Execute(async () => Map(await client.GetCurrentAsync(new GuestAccessRequest
        {
            TableSessionId = sessionId.ToString(),
            Token = GuestToken(sessionId)
        }, Options(ct))), ct);
    }

    [HttpGet("{id:guid}")]
    public Task<IActionResult> GetById(Guid sessionId, Guid id, CancellationToken ct)
    {
        return Execute(async () => Map(await client.GetByIdAsync(GuestRequest(sessionId, id), Options(ct))), ct);
    }

    [HttpGet("/api/restaurants/{restaurantId:guid}/payment-requests")]
    [RequireBearerToken]
    public Task<IActionResult> GetAll(Guid restaurantId, CancellationToken ct,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        return Execute(async () =>
        {
            var reply = await client.GetAllAsync(new PaymentServiceRequestList
            {
                RestaurantId = restaurantId.ToString(),
                Page = page,
                PageSize = pageSize
            }, Options(ct));

            return new
            {
                Items = reply.Items.Select(Map).ToArray(),
                reply.TotalCount,
                reply.Page,
                reply.PageSize
            };
        }, ct);
    }

    [HttpGet("{id:guid}/review")]
    [RequireBearerToken]
    public Task<IActionResult> GetReview(Guid sessionId, Guid id, CancellationToken ct)
    {
        return Execute(async () =>
        {
            var reply = await client.GetReviewAsync(StaffRequest(sessionId, id), Options(ct));
            return new
            {
                reply.PendingOrderCount,
                Items = reply.Items.Select(item => new
                {
                    OrderItemId = Guid.Parse(item.OrderItemId),
                    TableId = Guid.Parse(item.TableId),
                    item.Name,
                    item.Quantity,
                    item.Status
                }).ToArray()
            };
        }, ct);
    }

    [HttpPost("{id:guid}/acknowledge")]
    [RequireBearerToken]
    public Task<IActionResult> Acknowledge(Guid sessionId, Guid id, CancellationToken ct)
    {
        return Execute(async () => Map(await client.AcknowledgeAsync(StaffRequest(sessionId, id), Options(ct))), ct);
    }

    private static PaymentServiceRequest StaffRequest(Guid sessionId, Guid id)
    {
        return new PaymentServiceRequest
        {
            TableSessionId = sessionId.ToString(),
            ServiceRequestId = id.ToString()
        };
    }

    private PaymentServiceRequest GuestRequest(Guid sessionId, Guid id)
    {
        return new PaymentServiceRequest
        {
            TableSessionId = sessionId.ToString(),
            ServiceRequestId = id.ToString(),
            GuestToken = GuestToken(sessionId)
        };
    }

    private string GuestToken(Guid sessionId)
    {
        return Request.Cookies[$"guest_access_{sessionId:N}"] ?? string.Empty;
    }

    private static object Map(PaymentServiceRequestInfo value)
    {
        return new
        {
            ServiceRequestId = Guid.Parse(value.ServiceRequestId),
            TableSessionId = Guid.Parse(value.TableSessionId),
            RestaurantId = Guid.Parse(value.RestaurantId),
            value.Status,
            value.Type,
            CompletedAt = value.HasCompletedAt
                ? DateTimeOffset.Parse(value.CompletedAt, CultureInfo.InvariantCulture) : (DateTimeOffset?)null,
            RequestedAt = DateTimeOffset.Parse(value.RequestedAt, CultureInfo.InvariantCulture),
            AcknowledgedAt = value.HasAcknowledgedAt
                ? DateTimeOffset.Parse(value.AcknowledgedAt, CultureInfo.InvariantCulture) : (DateTimeOffset?)null,
            HandledByRef = value.HasHandledByRef ? Guid.Parse(value.HandledByRef) : (Guid?)null,
            Tables = value.Tables.Select(table => new
            {
                TableId = Guid.Parse(table.TableId),
                table.TableCode
            }).ToArray()
        };
    }
}
