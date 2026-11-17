using ApiGateway.Swagger;
using Backend.Contracts.Notifications;
using Microsoft.AspNetCore.Mvc;
using Shared.Common.Notifications;

namespace ApiGateway.Controllers.Notifications;

[ApiController]
[RequireBearerToken]
[Route("api/notifications")]
public sealed class NotificationController(NotificationGrpc.NotificationGrpcClient client) : NotificationControllerBase
{
    [HttpGet]
    public Task<IActionResult> GetAll([FromQuery] NotificationQuery query, CancellationToken ct)
    {
        return Execute(async () => Reply(await client.GetAllAsync(RequestFor(body: query), Options(ct))), ct);
    }

    [HttpGet("unread-count")]
    public Task<IActionResult> UnreadCount(CancellationToken ct)
    {
        return Execute(async () => Reply(await client.UnreadCountAsync(RequestFor(), Options(ct))), ct);
    }

    [HttpGet("{id:guid}")]
    public Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        return Execute(async () => Reply(await client.GetByIdAsync(RequestFor(id: id), Options(ct))), ct);
    }

    [HttpPatch("{id:guid}/read")]
    public Task<IActionResult> MarkRead(Guid id, CancellationToken ct)
    {
        return Execute(async () => Reply(await client.MarkReadAsync(RequestFor(id: id), Options(ct))), ct);
    }

    [HttpPatch("read-all")]
    public Task<IActionResult> MarkAllRead(CancellationToken ct)
    {
        return Execute(async () => Reply(await client.MarkReadAsync(RequestFor(), Options(ct))), ct);
    }
}
