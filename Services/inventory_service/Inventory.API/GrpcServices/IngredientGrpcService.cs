using Backend.Contracts.Inventory;
using Grpc.Core;
using Inventory.BLL.Services;
using Inventory.Common.DTOs;

namespace Inventory.API.GrpcServices;

public sealed class IngredientGrpcService(IngredientService service) : IngredientGrpc.IngredientGrpcBase
{
    public override async Task<IngredientInfo> Create(IngredientWriteRequest request, ServerCallContext context)
    {
        return InventoryMapping.Map(await service.Create(Write(request), context.CancellationToken));
    }

    public override async Task<IngredientInfo> Update(IngredientWriteRequest request, ServerCallContext context)
    {
        return InventoryMapping.Map(await service.Update(InventoryMapping.Id(request.Id), Write(request), context.CancellationToken));
    }

    public override async Task<IngredientInfo> SetStatus(IngredientStatusRequest request, ServerCallContext context)
    {
        return InventoryMapping.Map(await service.SetStatus(InventoryMapping.Id(request.Id), request.IsActive, context.CancellationToken));
    }

    public override async Task<IngredientInfo> GetById(InventoryIdRequest request, ServerCallContext context)
    {
        return InventoryMapping.Map(await service.GetById(InventoryMapping.Id(request.Id), context.CancellationToken));
    }

    public override async Task<IngredientPage> GetAll(IngredientListRequest request, ServerCallContext context)
    {
        var page = await service.GetAll(request.Page, request.PageSize, request.HasSearch ? request.Search : null,
            request.HasIsActive ? request.IsActive : null, request.LowStockOnly, context.CancellationToken);
        var reply = new IngredientPage
        {
            TotalCount = page.TotalCount,
            Page = page.Page,
            PageSize = page.PageSize
        };
        reply.Items.AddRange(page.Items.Select(InventoryMapping.Map));
        return reply;
    }

    private static IngredientWriteDTO Write(IngredientWriteRequest request)
    {
        return new IngredientWriteDTO
        {
            IngredientName = request.Name,
            BaseUnit = request.BaseUnit,
            LowStockThreshold = InventoryMapping.Decimal(request.LowStockThreshold)
        };
    }
}
