using Backend.Contracts.Notifications;
using Grpc.Core;
using Identity.BLL.Interfaces;
using Shared.Common.Notifications;

namespace Identity.API.GrpcServices;

public sealed class AnnouncementGrpcService(IAnnouncementService service) : AnnouncementGrpc.AnnouncementGrpcBase
{
    public override Task<NotificationReply> Create(NotificationRequest request, ServerCallContext context)
    {
        return NotificationRpc.Authenticated(context, async user => await service.Create(user, NotificationRpc.Id(request.RestaurantId), NotificationRpc.Body<AnnouncementWrite>(request.Json), context.CancellationToken));
    }

    public override Task<NotificationReply> GetAll(NotificationRequest request, ServerCallContext context)
    {
        return NotificationRpc.Authenticated(context, async user => await service.GetAll(user, NotificationRpc.Id(request.RestaurantId), NotificationRpc.Body<NotificationQuery>(request.Json).Page, NotificationRpc.Body<NotificationQuery>(request.Json).PageSize, context.CancellationToken));
    }

    public override Task<NotificationReply> GetById(NotificationRequest request, ServerCallContext context)
    {
        return NotificationRpc.Authenticated(context, async user => await service.GetById(user, NotificationRpc.Id(request.RestaurantId), NotificationRpc.Id(request.Id), context.CancellationToken));
    }

    public override Task<NotificationReply> Update(NotificationRequest request, ServerCallContext context)
    {
        return NotificationRpc.Authenticated(context, async user => await service.Update(user, NotificationRpc.Id(request.RestaurantId), NotificationRpc.Id(request.Id), NotificationRpc.Body<AnnouncementWrite>(request.Json), context.CancellationToken));
    }

    public override Task<NotificationReply> Send(NotificationRequest request, ServerCallContext context)
    {
        return NotificationRpc.Authenticated(context, async user => await service.Send(user, NotificationRpc.Id(request.RestaurantId), NotificationRpc.Id(request.Id), NotificationRpc.Id(request.Version), context.CancellationToken));
    }

    public override Task<NotificationReply> Delete(NotificationRequest request, ServerCallContext context)
    {
        return NotificationRpc.Authenticated(context, async user =>
        {
            await service.Delete(user, NotificationRpc.Id(request.RestaurantId), NotificationRpc.Id(request.Id), NotificationRpc.Id(request.Version), context.CancellationToken);
            return null;
        });
    }
}
