using System.Globalization;
using Backend.Contracts.Inventory;
using Grpc.Core;
using Inventory.BLL.Services;

namespace Inventory.API.GrpcServices;

public sealed class StockLotGrpcService(StockLotService service) : StockLotGrpc.StockLotGrpcBase
{
    public override async Task<StockLotPage> GetAll(MovementListRequest request, ServerCallContext context)
    {
        var page = await service.GetAll(InventoryMapping.Id(request.IngredientId), request.Page, request.PageSize, context.CancellationToken);
        var reply = new StockLotPage
        {
            TotalCount = page.TotalCount,
            Page = page.Page,
            PageSize = page.PageSize
        };
        reply.Items.AddRange(page.Items.Select(lot => new StockLotInfo
        {
            ReceiptItemId = lot.GoodsReceiptItemId.ToString(),
            IngredientId = lot.IngredientId.ToString(),
            ReceivedAt = DateTime.SpecifyKind(lot.ReceivedAt, DateTimeKind.Utc).ToString("O"),
            ExpiryDate = lot.ExpiryDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "",
            ReceivedQuantity = lot.ReceivedQuantity.ToString(CultureInfo.InvariantCulture),
            RemainingQuantity = lot.RemainingQuantity.ToString(CultureInfo.InvariantCulture),
            ExpiryStatus = lot.ExpiryStatus,
            CanIssue = lot.CanIssue
        }));
        return reply;
    }
}
