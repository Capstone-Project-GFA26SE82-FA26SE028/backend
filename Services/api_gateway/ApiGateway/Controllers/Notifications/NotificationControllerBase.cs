using System.Text.Json;
using ApiGateway.Controllers.IdentityController;
using Backend.Contracts.Notifications;
using Microsoft.AspNetCore.Mvc;
using Shared.Common.DTOs;

namespace ApiGateway.Controllers.Notifications;

public abstract class NotificationControllerBase : IdentityControllerBase
{
    protected static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    protected static NotificationRequest RequestFor(Guid? restaurantId = null, Guid? id = null, object? body = null, Guid? version = null)
    {
        return new NotificationRequest
        {
            RestaurantId = restaurantId?.ToString() ?? "",
            Id = id?.ToString() ?? "",
            Json = body is null ? "" : JsonSerializer.Serialize(body, Json),
            Version = version?.ToString() ?? ""
        };
    }

    protected IActionResult Reply(NotificationReply reply, int successStatus = 200)
    {
        var success = reply.StatusCode is >= 200 and < 300;
        var status = success ? successStatus : reply.StatusCode;
        return StatusCode(status, new ResponseDTO
        {
            StatusCode = status,
            IsSuccess = success,
            Message = reply.Message,
            Result = string.IsNullOrEmpty(reply.Json) ? null : JsonSerializer.Deserialize<JsonElement>(reply.Json)
        });
    }
}
