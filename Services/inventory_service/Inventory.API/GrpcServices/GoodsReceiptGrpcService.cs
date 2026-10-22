using Backend.Contracts.Inventory;
using Grpc.Core;
using Inventory.BLL.Services;
using Inventory.Common.DTOs;

namespace Inventory.API.GrpcServices;

public sealed class GoodsReceiptGrpcService(GoodsReceiptService service) : GoodsReceiptGrpc.GoodsReceiptGrpcBase
{
    public override async Task<ReceiptInfo> Create(ReceiptWriteRequest request, ServerCallContext context)
    {
        return InventoryMapping.Map(await service.Create(new ReceiptWriteDTO
        {
            GoodsReceiptId = InventoryMapping.Id(request.Id),
            Note = request.Note,
            Items = request.Items.Select(line => new ReceiptLineDTO
            {
                IngredientId = InventoryMapping.Id(line.IngredientId),
                UnitId = !string.IsNullOrEmpty(line.UnitId) ? InventoryMapping.Id(line.UnitId) : null,
                ReceivedQuantity = InventoryMapping.Decimal(line.ReceivedQuantity),
                ExpiryDate = InventoryMapping.Date(line.ExpiryDate)
            }).ToList()
        }, context.CancellationToken));
    }

    public override async Task<ReceiptInfo> Confirm(InventoryIdRequest request, ServerCallContext context)
    {
        return InventoryMapping.Map(await service.Confirm(InventoryMapping.Id(request.Id), context.CancellationToken));
    }

    public override async Task<ReceiptInfo> GetById(InventoryIdRequest request, ServerCallContext context)
    {
        return InventoryMapping.Map(await service.GetById(InventoryMapping.Id(request.Id), context.CancellationToken));
    }

    public override async Task<ReceiptPage> GetAll(InventoryPageRequest request, ServerCallContext context)
    {
        var page = await service.GetAll(request.Page, request.PageSize, context.CancellationToken);
        var reply = new ReceiptPage
        {
            TotalCount = page.TotalCount,
            Page = page.Page,
            PageSize = page.PageSize
        };
        reply.Items.AddRange(page.Items.Select(InventoryMapping.Map));
        return reply;
    }
}
