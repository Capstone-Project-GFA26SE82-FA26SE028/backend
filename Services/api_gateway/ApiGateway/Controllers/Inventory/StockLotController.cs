using System.Globalization;
using ApiGateway.Swagger;
using Backend.Contracts.Inventory;
using Microsoft.AspNetCore.Mvc;

namespace ApiGateway.Controllers.Inventory;

[ApiController]
[Route("api/ingredients/{ingredientId:guid}/lots")]
[RequireBearerToken]
public sealed class StockLotController(StockLotGrpc.StockLotGrpcClient client) : InventoryControllerBase
{
    [HttpGet]
    public Task<IActionResult> GetAll(Guid ingredientId, CancellationToken ct, int page = 1, int pageSize = 20)
    {
        return Execute(async () =>
        {
            var reply = await client.GetAllAsync(new MovementListRequest
            {
                IngredientId = ingredientId.ToString(),
                Page = page,
                PageSize = pageSize
            }, Options(ct));
            return new
            {
                Items = reply.Items.Select(lot => new
                {
                    GoodsReceiptItemId = lot.ReceiptItemId,
                    lot.IngredientId,
                    lot.ReceivedAt,
                    ExpiryDate = string.IsNullOrEmpty(lot.ExpiryDate) ? null : lot.ExpiryDate,
                    ReceivedQuantity = decimal.Parse(lot.ReceivedQuantity, CultureInfo.InvariantCulture),
                    RemainingQuantity = decimal.Parse(lot.RemainingQuantity, CultureInfo.InvariantCulture),
                    lot.ExpiryStatus,
                    lot.CanIssue
                }).ToList(),
                reply.TotalCount,
                reply.Page,
                reply.PageSize
            };
        }, ct);
    }
}
