using ApiGateway.Swagger;
using Backend.Contracts.Notifications;
using Microsoft.AspNetCore.Mvc;
using Shared.Common.Notifications;

namespace ApiGateway.Controllers.Notifications;

public sealed class AnnouncementSend
{
    public Guid Version { get; set; }
}

[ApiController]
[RequireBearerToken]
[RequestSizeLimit(16384)]
[Route("api/restaurants/{restaurantId:guid}/announcements")]
public sealed class AnnouncementController(AnnouncementGrpc.AnnouncementGrpcClient client) : NotificationControllerBase
{
    [HttpPost]
    public Task<IActionResult> Create(Guid restaurantId, AnnouncementWrite body, CancellationToken ct)
    {
        return Execute(async () => Reply(await client.CreateAsync(RequestFor(restaurantId, body: body), Options(ct)), 201), ct);
    }

    [HttpGet]
    public Task<IActionResult> GetAll(Guid restaurantId, CancellationToken ct, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        return Execute(async () => Reply(await client.GetAllAsync(RequestFor(restaurantId, body: new NotificationQuery
        {
            Page = page,
            PageSize = pageSize
        }), Options(ct))), ct);
    }

    [HttpGet("{id:guid}")]
    public Task<IActionResult> GetById(Guid restaurantId, Guid id, CancellationToken ct)
    {
        return Execute(async () => Reply(await client.GetByIdAsync(RequestFor(restaurantId, id), Options(ct))), ct);
    }

    [HttpPut("{id:guid}")]
    public Task<IActionResult> Update(Guid restaurantId, Guid id, AnnouncementWrite body, CancellationToken ct)
    {
        return Execute(async () => Reply(await client.UpdateAsync(RequestFor(restaurantId, id, body), Options(ct))), ct);
    }

    [HttpDelete("{id:guid}")]
    public Task<IActionResult> Delete(Guid restaurantId, Guid id, [FromQuery] Guid version, CancellationToken ct)
    {
        return Execute(async () => Reply(await client.DeleteAsync(RequestFor(restaurantId, id, version: version), Options(ct))), ct);
    }

    [HttpPost("{id:guid}/send")]
    public Task<IActionResult> Send(Guid restaurantId, Guid id, AnnouncementSend body, CancellationToken ct)
    {
        return Execute(async () => Reply(await client.SendAsync(RequestFor(restaurantId, id, version: body.Version), Options(ct))), ct);
    }
}
