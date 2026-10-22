using System.Globalization;
using Backend.Contracts.Inventory;
using Grpc.Core;
using Inventory.BLL.Services;
using Inventory.DAL.Models;

namespace Inventory.API.GrpcServices;

public sealed class IngredientUnitGrpcService(IngredientUnitService service) : IngredientUnitGrpc.IngredientUnitGrpcBase
{
    public override async Task<ConversionInfo> Update(ConversionWriteRequest request, ServerCallContext context)
    {
        return Map(await service.Update(InventoryMapping.Id(request.IngredientId), InventoryMapping.Id(request.UnitId),
            InventoryMapping.Decimal(request.Factor), context.CancellationToken));
    }

    public override async Task<ConversionList> GetAll(InventoryIdRequest request, ServerCallContext context)
    {
        var reply = new ConversionList();
        reply.Items.AddRange((await service.GetAll(InventoryMapping.Id(request.Id), context.CancellationToken)).Select(Map));
        return reply;
    }

    public override async Task<ConversionInfo> Resolve(ConversionRequest request, ServerCallContext context)
    {
        var factor = await service.Resolve(InventoryMapping.Id(request.IngredientId), InventoryMapping.Id(request.UnitId), context.CancellationToken);
        return new ConversionInfo
        {
            IngredientId = request.IngredientId,
            UnitId = request.UnitId,
            Factor = factor.ToString(CultureInfo.InvariantCulture)
        };
    }

    private static ConversionInfo Map(IngredientUnit conversion)
    {
        return new ConversionInfo
        {
            IngredientId = conversion.IngredientId.ToString(),
            UnitId = conversion.UnitId.ToString(),
            Factor = conversion.ConversionFactor.ToString(CultureInfo.InvariantCulture)
        };
    }
}
