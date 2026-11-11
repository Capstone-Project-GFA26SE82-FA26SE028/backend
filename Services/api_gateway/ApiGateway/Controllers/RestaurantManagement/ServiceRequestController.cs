using System.Globalization;
using Backend.Contracts.Restaurant;
using Microsoft.AspNetCore.Mvc;

namespace ApiGateway.Controllers.RestaurantManagement;

[ApiController]
[Route("api/service-requests")]
[ApiGateway.Swagger.RequireBearerToken]
public sealed class ServiceRequestController(ServiceRequestGrpc.ServiceRequestGrpcClient client) : ManagementControllerBase
{
    [HttpGet]
    public Task<IActionResult> GetAll([FromQuery] Guid restaurantId, [FromQuery] string? status,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        return Execute(async () =>
        {
            var request = new ServiceRequestListRequest
            {
                RestaurantId = restaurantId.ToString(),
                Page = page,
                PageSize = pageSize
            };
            if (status is not null)
            {
                request.Status = status;
            }

            var result = await client.GetAllAsync(request, Options(ct));
            return new
            {
                Items = result.Items.Select(Map).ToArray(),
                result.TotalCount,
                result.Page,
                result.PageSize
            };
        }, ct);
    }

    [HttpGet("/api/table-sessions/{sessionId:guid}/service-requests/{id:guid}")]
    public Task<IActionResult> GetById(Guid sessionId, Guid id, CancellationToken ct)
    {
        return Execute(async () => Map(await client.GetByIdAsync(Write(sessionId, id), Options(ct))), ct);
    }

    [HttpPost("/api/table-sessions/{sessionId:guid}/service-requests/{id:guid}/acknowledge")]
    public Task<IActionResult> Acknowledge(Guid sessionId, Guid id, CancellationToken ct)
    {
        return Execute(async () => Map(await client.AcknowledgeAsync(Write(sessionId, id), Options(ct))), ct);
    }

    [HttpPost("/api/table-sessions/{sessionId:guid}/service-requests/{id:guid}/review")]
    public Task<IActionResult> Review(Guid sessionId, Guid id, CancellationToken ct)
    {
        return Execute(async () => Map(await client.ReviewAsync(Write(sessionId, id), Options(ct))), ct);
    }

    private static PaymentServiceRequest Write(Guid sessionId, Guid id)
    {
        return new PaymentServiceRequest
        {
            TableSessionId = sessionId.ToString(),
            ServiceRequestId = id.ToString()
        };
    }

    private static object Map(ServiceRequestInfo value)
    {
        return new
        {
            ServiceRequestId = Guid.Parse(value.ServiceRequestId),
            TableSessionId = Guid.Parse(value.TableSessionId),
            RestaurantId = Guid.Parse(value.RestaurantId),
            value.Type,
            value.Status,
            RequestedAt = DateTimeOffset.Parse(value.RequestedAt, CultureInfo.InvariantCulture),
            AcknowledgedAt = value.HasAcknowledgedAt ? DateTimeOffset.Parse(value.AcknowledgedAt, CultureInfo.InvariantCulture) : (DateTimeOffset?)null,
            CompletedAt = value.HasCompletedAt ? DateTimeOffset.Parse(value.CompletedAt, CultureInfo.InvariantCulture) : (DateTimeOffset?)null,
            HandledByRef = value.HasHandledByRef ? Guid.Parse(value.HandledByRef) : (Guid?)null
        };
    }
}
