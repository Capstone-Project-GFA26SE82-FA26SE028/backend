using Backend.Contracts.Inventory;
using Grpc.Core;
using Inventory.BLL.Services;

namespace Inventory.API.GrpcServices;

public sealed class StockMovementGrpcService(StockMovementService service) : StockMovementGrpc.StockMovementGrpcBase
{
    public override async Task<MovementPage> GetAll(MovementListRequest request, ServerCallContext context)
    {
        var page = await service.GetAll(InventoryMapping.Id(request.IngredientId), request.Page, request.PageSize, context.CancellationToken);
        var reply = new MovementPage
        {
            TotalCount = page.TotalCount,
            Page = page.Page,
            PageSize = page.PageSize
        };
        reply.Items.AddRange(page.Items.Select(InventoryMapping.Map));
        return reply;
    }
}
