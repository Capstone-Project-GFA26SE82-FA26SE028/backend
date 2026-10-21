using Backend.Contracts.Ordering;
using Grpc.Core;
using Ordering.BLL.Interfaces;
using Ordering.Common.DTOs;

namespace Ordering.API.GrpcServices;

public sealed class KitchenStationGrpcService(IKitchenStationService service) : KitchenStationGrpc.KitchenStationGrpcBase
{
    public override async Task<KitchenStationInfo> Create(KitchenStationWriteRequest request, ServerCallContext context)
    {
        return Map(await service.Create(Write(request), context.CancellationToken));
    }

    public override async Task<KitchenStationInfo> Update(KitchenStationWriteRequest request, ServerCallContext context)
    {
        return Map(await service.Update(Guid.Parse(request.Id), Write(request), context.CancellationToken));
    }

    public override async Task<KitchenStationInfo> GetById(KitchenStationIdRequest request, ServerCallContext context)
    {
        return Map(await service.GetById(Guid.Parse(request.Id), context.CancellationToken));
    }

    public override async Task<KitchenStationInfo> SetStatus(KitchenStationStatusRequest request, ServerCallContext context)
    {
        return Map(await service.SetStatus(Guid.Parse(request.Id), request.IsActive, context.CancellationToken));
    }

    public override async Task<KitchenStationPage> GetAll(KitchenStationListRequest request, ServerCallContext context)
    {
        var page = await service.GetAll(request.Page, request.PageSize,
            request.HasSearch ? request.Search : null,
            request.HasIsActive ? request.IsActive : null, context.CancellationToken);
        var reply = new KitchenStationPage
        {
            TotalCount = page.TotalCount,
            Page = page.Page,
            PageSize = page.PageSize
        };
        reply.Items.AddRange(page.Items.Select(Map));
        return reply;
    }

    private static KitchenStationWriteDTO Write(KitchenStationWriteRequest request)
    {
        return new KitchenStationWriteDTO
        {
            KitchenStationCode = request.Code,
            KitchenStationName = request.Name
        };
    }

    private static KitchenStationInfo Map(KitchenStationDTO station)
    {
        return new KitchenStationInfo
        {
            Id = station.KitchenStationId.ToString(),
            Code = station.KitchenStationCode,
            Name = station.KitchenStationName,
            IsActive = station.IsActive
        };
    }
}
