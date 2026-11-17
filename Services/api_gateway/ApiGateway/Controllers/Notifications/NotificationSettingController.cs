using System.ComponentModel.DataAnnotations;
using ApiGateway.Swagger;
using Backend.Contracts.Notifications;
using Microsoft.AspNetCore.Mvc;

namespace ApiGateway.Controllers.Notifications;

public sealed class NotificationSettingWrite
{
    [Required]
    public bool? Enabled { get; set; }
}

[ApiController]
[RequireBearerToken]
[Route("api/restaurants/{restaurantId:guid}/notification-settings")]
public sealed class NotificationSettingController(NotificationSettingGrpc.NotificationSettingGrpcClient client) : NotificationControllerBase
{
    [HttpGet]
    public Task<IActionResult> GetAll(Guid restaurantId, CancellationToken ct)
    {
        return Execute(async () => Reply(await client.GetAllAsync(RequestFor(restaurantId), Options(ct))), ct);
    }

    [HttpPatch("{type}")]
    public Task<IActionResult> Update(Guid restaurantId, string type, NotificationSettingWrite body, CancellationToken ct)
    {
        return Execute(async () => Reply(await client.UpdateAsync(new NotificationRequest
        {
            RestaurantId = restaurantId.ToString(),
            Type = type,
            Enabled = body.Enabled!.Value
        }, Options(ct))), ct);
    }
}
