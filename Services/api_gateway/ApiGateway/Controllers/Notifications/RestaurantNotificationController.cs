using ApiGateway.Swagger;
using Backend.Contracts.Notifications;
using Microsoft.AspNetCore.Mvc;
using Shared.Common.Notifications;

namespace ApiGateway.Controllers.Notifications;

[ApiController]
[RequireBearerToken]
[Route("api/restaurants/{restaurantId:guid}/notifications")]
public sealed class RestaurantNotificationController(NotificationGrpc.NotificationGrpcClient client) : NotificationControllerBase
{
    [HttpGet]
    public Task<IActionResult> GetAll(Guid restaurantId, [FromQuery] NotificationQuery query, CancellationToken ct)
    {
        return Execute(async () => Reply(await client.GetAllAsync(RequestFor(restaurantId, body: query), Options(ct))), ct);
    }

    [HttpGet("{id:guid}")]
    public Task<IActionResult> GetById(Guid restaurantId, Guid id, CancellationToken ct)
    {
        return Execute(async () => Reply(await client.GetByIdAsync(RequestFor(restaurantId, id), Options(ct))), ct);
    }
}
