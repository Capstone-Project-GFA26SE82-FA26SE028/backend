using System.Text.Json;
using Backend.Contracts.Notifications;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using Shared.Common.Notifications;

namespace Identity.API.GrpcServices;

internal static class NotificationRpc
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static Guid Id(string value)
    {
        return Guid.TryParse(value, out var id) && id != Guid.Empty ? id
            : throw new NotificationException(400, "Mã định danh không hợp lệ.");
    }

    public static Guid? OptionalId(string value)
    {
        return string.IsNullOrEmpty(value) ? null : Id(value);
    }

    public static T Body<T>(string value) where T : new()
    {
        return string.IsNullOrEmpty(value) ? new T() : JsonSerializer.Deserialize<T>(value, Json)
            ?? throw new NotificationException(400, "Dữ liệu không hợp lệ.");
    }

    public static async Task<NotificationReply> Execute(Func<Task<object?>> action)
    {
        try
        {
            return new NotificationReply
            {
                StatusCode = 200,
                Message = "Thành công.",
                Json = JsonSerializer.Serialize(await action(), Json)
            };
        }
        catch (NotificationException exception)
        {
            return new NotificationReply
            {
                StatusCode = exception.Status,
                Message = exception.Message
            };
        }
        catch (JsonException)
        {
            return new NotificationReply
            {
                StatusCode = 400,
                Message = "Dữ liệu không hợp lệ."
            };
        }
        catch (DbUpdateException)
        {
            return new NotificationReply
            {
                StatusCode = 409,
                Message = "Dữ liệu đã thay đổi. Vui lòng tải lại và thử lại."
            };
        }
    }

    public static async Task<NotificationReply> Authenticated(ServerCallContext context, Func<Guid, Task<object?>> action)
    {
        return await Execute(async () =>
        {
            var actor = await GrpcAuthentication.RequireUser(context);
            return await action(actor.UserId);
        });
    }
}
