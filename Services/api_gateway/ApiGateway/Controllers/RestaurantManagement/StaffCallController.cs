using System.Globalization;
using ApiGateway.Swagger;
using Backend.Contracts.Restaurant;
using Microsoft.AspNetCore.Mvc;
using Shared.Common.DTOs;

namespace ApiGateway.Controllers.RestaurantManagement;

[ApiController]
[Route("api/table-sessions/{sessionId:guid}/staff-calls")]
public sealed class StaffCallController(ServiceRequestGrpc.ServiceRequestGrpcClient client) : ManagementControllerBase
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

            return Map(await client.CreateStaffCallAsync(GuestRequest(sessionId, id), Options(ct)));
        }, ct);
    }

    [HttpGet("current")]
    public Task<IActionResult> GetCurrent(Guid sessionId, CancellationToken ct)
    {
        return Execute(async () => Map(await client.GetCurrentStaffCallAsync(new GuestAccessRequest
        {
            TableSessionId = sessionId.ToString(),
            Token = GuestToken(sessionId)
        }, Options(ct))), ct);
    }

    [HttpGet("{id:guid}")]
    public Task<IActionResult> GetById(Guid sessionId, Guid id, CancellationToken ct)
    {
        return Execute(async () => Map(await client.GetStaffCallByIdAsync(GuestRequest(sessionId, id), Options(ct))), ct);
    }

    [HttpGet("/api/restaurants/{restaurantId:guid}/staff-calls")]
    [RequireBearerToken]
    public Task<IActionResult> GetAll(Guid restaurantId, CancellationToken ct,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        return Execute(async () =>
        {
            var reply = await client.GetStaffCallsAsync(new PaymentServiceRequestList
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

    [HttpGet("/api/restaurants/{restaurantId:guid}/staff-calls/{id:guid}")]
    [RequireBearerToken]
    public Task<IActionResult> GetForStaff(Guid restaurantId, Guid id, CancellationToken ct)
    {
        return Execute(async () => Map(await client.GetStaffCallForStaffAsync(new StaffCallAccessRequest
        {
            RestaurantId = restaurantId.ToString(),
            ServiceRequestId = id.ToString()
        }, Options(ct))), ct);
    }

    [HttpPost("{id:guid}/complete")]
    [RequireBearerToken]
    public Task<IActionResult> Complete(Guid sessionId, Guid id, CancellationToken ct)
    {
        return Execute(async () => Map(await client.CompleteStaffCallAsync(StaffRequest(sessionId, id), Options(ct))), ct);
    }

    [HttpPost("{id:guid}/acknowledge")]
    [RequireBearerToken]
    public Task<IActionResult> Acknowledge(Guid sessionId, Guid id, CancellationToken ct)
    {
        return Execute(async () => Map(await client.AcknowledgeStaffCallAsync(StaffRequest(sessionId, id), Options(ct))), ct);
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
