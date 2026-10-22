using Inventory.Common.DTOs;
using Inventory.DAL.Models;
using Microsoft.EntityFrameworkCore;

namespace Inventory.DAL.Repositories;

public sealed class StockMovementRepository(InventoryDbContext db)
{
    public async Task CreateAsync(StockMovement movement, CancellationToken ct)
    {
        await db.StockMovements.AddAsync(movement, ct);
    }

    public async Task<PageDTO<StockMovement>> GetAllAsync(Guid ingredientId, int page, int pageSize, CancellationToken ct)
    {
        var query = db.StockMovements.AsNoTracking().Where(x => x.IngredientId == ingredientId);
        return new PageDTO<StockMovement>
        {
            Items = await query.OrderByDescending(x => x.CreatedAt).ThenBy(x => x.StockMovementId)
                .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct),
            TotalCount = await query.CountAsync(ct),
            Page = page,
            PageSize = pageSize
        };
    }
}
