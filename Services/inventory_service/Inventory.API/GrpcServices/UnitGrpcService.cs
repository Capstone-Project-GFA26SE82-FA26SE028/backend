using Backend.Contracts.Inventory;
using Grpc.Core;
using Inventory.BLL.Services;
using Inventory.DAL.Models;

namespace Inventory.API.GrpcServices;

public sealed class UnitGrpcService(UnitService service) : UnitGrpc.UnitGrpcBase
{
    public override async Task<UnitInfo> Create(UnitWriteRequest request, ServerCallContext context)
    {
        return Map(await service.Create(request.Code, request.Name, context.CancellationToken));
    }

    public override async Task<UnitList> GetAll(UnitListRequest request, ServerCallContext context)
    {
        var reply = new UnitList();
        reply.Items.AddRange((await service.GetAll(context.CancellationToken)).Select(Map));
        return reply;
    }

    private static UnitInfo Map(Unit unit)
    {
        return new UnitInfo
        {
            Id = unit.UnitId.ToString(),
            Code = unit.Code,
            Name = unit.Name
        };
    }
}
