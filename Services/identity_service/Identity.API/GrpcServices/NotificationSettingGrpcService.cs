using Backend.Contracts.Notifications;
using Grpc.Core;
using Identity.BLL.Interfaces;

namespace Identity.API.GrpcServices;

public sealed class NotificationSettingGrpcService(INotificationSettingService service) : NotificationSettingGrpc.NotificationSettingGrpcBase
{
    public override Task<NotificationReply> GetAll(NotificationRequest request, ServerCallContext context)
    {
        return NotificationRpc.Authenticated(context, async user => await service.GetAll(user, NotificationRpc.Id(request.RestaurantId), context.CancellationToken));
    }

    public override Task<NotificationReply> Update(NotificationRequest request, ServerCallContext context)
    {
        return NotificationRpc.Authenticated(context, async user => await service.Update(user, NotificationRpc.Id(request.RestaurantId), request.Type, request.Enabled, context.CancellationToken));
    }
}
