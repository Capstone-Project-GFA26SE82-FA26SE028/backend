using ApiGateway.Swagger;
using Backend.Contracts.Inventory;
using Microsoft.AspNetCore.Mvc;

namespace ApiGateway.Controllers.Inventory;

[ApiController]
[Route("api/ingredients/{ingredientId:guid}/stock-movements")]
[RequireBearerToken]
public sealed class StockMovementController(StockMovementGrpc.StockMovementGrpcClient client) : InventoryControllerBase
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
                Items = reply.Items.Select(InventoryMapping.Map).ToList(),
                reply.TotalCount,
                reply.Page,
                reply.PageSize
            };
        }, ct);
    }
}
