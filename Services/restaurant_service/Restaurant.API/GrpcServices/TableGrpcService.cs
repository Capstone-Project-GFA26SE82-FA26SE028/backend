using Backend.Contracts.Restaurant;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Restaurant.API.Management;
using Restaurant.BLL.Interfaces;
using Restaurant.Common.DTOs;

namespace Restaurant.API.GrpcServices;

public sealed class TableGrpcService(ITableService service) : TableGrpc.TableGrpcBase
{
    public override async Task<TablePage> GetAll(ListRequest request, ServerCallContext context)
    {
        var page = await service.GetAll(ManagementMapping.ToDto(request), context.CancellationToken);
        var reply = new TablePage
        {
            TotalCount = page.TotalCount,
            Page = page.Page,
            PageSize = page.PageSize
        };
        reply.Items.AddRange(page.Items.Select(ManagementMapping.ToWire));
        return reply;
    }

    public override async Task<TableInfo> GetById(IdRequest request, ServerCallContext context)
    {
        return ManagementMapping.ToWire(await service.GetById(ManagementMapping.Id(request.Id), context.CancellationToken));
    }

    public override async Task<TableInfo> Create(TableInfo request, ServerCallContext context)
    {
        return ManagementMapping.ToWire(await service.Create(ManagementMapping.ToDto(request), context.CancellationToken));
    }

    public override async Task<TableInfo> Update(TableInfo request, ServerCallContext context)
    {
        return ManagementMapping.ToWire(await service.Update(ManagementMapping.Id(request.TableId),
            ManagementMapping.ToDto(request), context.CancellationToken));
    }

    public override async Task<Empty> Delete(IdRequest request, ServerCallContext context)
    {
        await service.Delete(ManagementMapping.Id(request.Id), request.Version, context.CancellationToken);
        return new Empty();
    }

    public override async Task<TableInfo> SetStatus(StatusRequest request, ServerCallContext context)
    {
        return ManagementMapping.ToWire(await service.SetStatus(ManagementMapping.Id(request.Id), new StatusDto
        {
            IsActive = request.IsActive,
            Version = request.Version
        }, context.CancellationToken));
    }

    public override async Task<TableInfo> AssignArea(AssignAreaRequest request, ServerCallContext context)
    {
        return ManagementMapping.ToWire(await service.AssignArea(ManagementMapping.Id(request.Id), new AssignAreaDto
        {
            AreaId = ManagementMapping.Id(request.AreaId),
            Version = request.Version
        }, context.CancellationToken));
    }
}
