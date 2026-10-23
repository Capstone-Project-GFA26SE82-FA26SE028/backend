using Backend.Contracts.Ordering;
using Grpc.Core;
using Ordering.BLL.Interfaces;

namespace Ordering.API.GrpcServices;

public sealed class KitchenTaskGrpcService(IKitchenTaskService service) : KitchenTaskGrpc.KitchenTaskGrpcBase
{
    public override async Task<KitchenTaskPage> GetAll(KitchenTaskListRequest request, ServerCallContext context)
    {
        Guid? stationId = request.HasKitchenStationId && Guid.TryParse(request.KitchenStationId, out var parsedStation)
            ? parsedStation : null;
        Guid? orderId = request.HasOrderId && Guid.TryParse(request.OrderId, out var parsedOrder)
            ? parsedOrder : null;
        Guid? sessionId = request.HasTableSessionId && Guid.TryParse(request.TableSessionId, out var parsedSession)
            ? parsedSession : null;

        var page = await service.GetAll(
            request.Page,
            request.PageSize,
            stationId,
            request.HasStatus ? request.Status : null,
            orderId,
            sessionId,
            context.CancellationToken);

        var reply = new KitchenTaskPage
        {
            TotalCount = page.TotalCount,
            Page = page.Page,
            PageSize = page.PageSize
        };
        reply.Items.AddRange(page.Items.Select(OrderingMapping.Map));
        return reply;
    }

    public override async Task<KitchenTaskInfo> GetById(KitchenTaskIdRequest request, ServerCallContext context)
    {
        return OrderingMapping.Map(await service.GetById(
            Guid.Parse(request.Id),
            context.CancellationToken));
    }

    public override async Task<KitchenTaskInfo> SetStatus(KitchenTaskStatusRequest request, ServerCallContext context)
    {
        return OrderingMapping.Map(await service.SetStatus(
            Guid.Parse(request.Id),
            request.Status,
            request.HasVersion ? request.Version : null,
            context.CancellationToken));
    }

    public override async Task<KitchenTaskInfo> SetPriority(KitchenTaskPriorityRequest request, ServerCallContext context)
    {
        return OrderingMapping.Map(await service.SetPriority(
            Guid.Parse(request.Id),
            request.Priority,
            request.HasVersion ? request.Version : null,
            context.CancellationToken));
    }

    public override async Task<KitchenSummaryReply> GetSummary(KitchenSummaryRequest request, ServerCallContext context)
    {
        Guid? stationId = request.HasKitchenStationId && Guid.TryParse(request.KitchenStationId, out var parsedStation)
            ? parsedStation : null;

        return OrderingMapping.Map(await service.GetSummary(
            stationId,
            context.CancellationToken));
    }
}

