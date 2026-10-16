using Backend.Contracts.Restaurant;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Restaurant.API.Management;
using Restaurant.BLL.Interfaces;
using Restaurant.Common.DTOs;

namespace Restaurant.API.GrpcServices;

public sealed class AreaGrpcService(IAreaService service) : AreaGrpc.AreaGrpcBase
{
    public override async Task<AreaPage> GetAll(ListRequest request, ServerCallContext context)
    {
        var page = await service.GetAll(ManagementMapping.ToDto(request), context.CancellationToken);
        var reply = new AreaPage
        {
            TotalCount = page.TotalCount,
            Page = page.Page,
            PageSize = page.PageSize
        };
        reply.Items.AddRange(page.Items.Select(ManagementMapping.ToWire));
        return reply;
    }

    public override async Task<AreaInfo> GetById(IdRequest request, ServerCallContext context)
    {
        return ManagementMapping.ToWire(await service.GetById(ManagementMapping.Id(request.Id), context.CancellationToken));
    }

    public override async Task<AreaInfo> Create(AreaInfo request, ServerCallContext context)
    {
        return ManagementMapping.ToWire(await service.Create(ManagementMapping.ToDto(request), context.CancellationToken));
    }

    public override async Task<AreaInfo> Update(AreaInfo request, ServerCallContext context)
    {
        return ManagementMapping.ToWire(await service.Update(ManagementMapping.Id(request.AreaId),
            ManagementMapping.ToDto(request), context.CancellationToken));
    }

    public override async Task<Empty> Delete(IdRequest request, ServerCallContext context)
    {
        await service.Delete(ManagementMapping.Id(request.Id), context.CancellationToken);
        return new Empty();
    }

    public override async Task<AreaInfo> SetStatus(StatusRequest request, ServerCallContext context)
    {
        return ManagementMapping.ToWire(await service.SetStatus(ManagementMapping.Id(request.Id), request.IsActive, context.CancellationToken));
    }
}
