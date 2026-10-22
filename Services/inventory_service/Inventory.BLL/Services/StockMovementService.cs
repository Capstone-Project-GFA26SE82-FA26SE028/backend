using Inventory.BLL.Interfaces;
using Inventory.Common;
using Inventory.Common.DTOs;
using Inventory.DAL.Models;
using Inventory.DAL.Repositories;

namespace Inventory.BLL.Services;

public sealed class StockMovementService(StockMovementRepository repository, IngredientRepository ingredients, IInventoryAccess access)
{
    public async Task<PageDTO<StockMovement>> GetAll(Guid ingredientId, int page, int pageSize, CancellationToken ct)
    {
        await access.RequireAsync("Read", ct);
        InventoryValidation.Id(ingredientId);
        InventoryValidation.Page(page, pageSize);
        if (await ingredients.GetByIdAsync(ingredientId, ct) is null)
        {
            throw new InventoryException(404, "Ingredient not found.");
        }
        return await repository.GetAllAsync(ingredientId, page, pageSize, ct);
    }
}
