using System.Security.Cryptography;
using System.Text;
using Backend.Contracts.Notifications;
using Grpc.Core;
using Identity.BLL.Interfaces;
using Shared.Common.Notifications;

namespace Identity.API.GrpcServices;

public sealed class NotificationGrpcService(INotificationService service) : NotificationGrpc.NotificationGrpcBase
{
    public override Task<NotificationReply> GetAll(NotificationRequest request, ServerCallContext context)
    {
        return NotificationRpc.Authenticated(context, async user => await service.GetAll(user,
            NotificationRpc.OptionalId(request.RestaurantId), NotificationRpc.Body<NotificationQuery>(request.Json), context.CancellationToken));
    }

    public override Task<NotificationReply> GetById(NotificationRequest request, ServerCallContext context)
    {
        return NotificationRpc.Authenticated(context, async user => await service.GetById(user,
            NotificationRpc.OptionalId(request.RestaurantId), NotificationRpc.Id(request.Id), context.CancellationToken));
    }

    public override Task<NotificationReply> UnreadCount(NotificationRequest request, ServerCallContext context)
    {
        return NotificationRpc.Authenticated(context, async user => new { Count = await service.UnreadCount(user, context.CancellationToken) });
    }

    public override Task<NotificationReply> MarkRead(NotificationRequest request, ServerCallContext context)
    {
        return NotificationRpc.Authenticated(context, async user =>
        {
            await service.MarkRead(user, NotificationRpc.OptionalId(request.Id), context.CancellationToken);
            return null;
        });
    }

}
