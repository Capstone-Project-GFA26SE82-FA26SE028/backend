using Inventory.Common.DTOs;
using Inventory.DAL.Models;
using Microsoft.EntityFrameworkCore;

namespace Inventory.DAL.Repositories;

public sealed class IngredientRepository(InventoryDbContext db)
{
    public Task<Ingredient?> GetByIdAsync(Guid id, CancellationToken ct)
    {
        return db.Ingredients.SingleOrDefaultAsync(x => x.IngredientId == id, ct);
    }

    public async Task CreateAsync(Ingredient ingredient, CancellationToken ct)
    {
        await db.Ingredients.AddAsync(ingredient, ct);
    }

    public async Task<PageDTO<Ingredient>> GetAllAsync(int page, int pageSize, string? search, bool? active, bool lowStock, CancellationToken ct)
    {
        var query = db.Ingredients.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(x => x.IngredientName.Contains(search) || x.IngredientCode.Contains(search));
        }
        if (active.HasValue)
        {
            query = query.Where(x => x.IsActive == active.Value);
        }
        if (lowStock)
        {
            query = query.Where(x => x.StockQuantity <= x.LowStockThreshold);
        }
        return new PageDTO<Ingredient>
        {
            Items = await query.OrderBy(x => x.IngredientCode).ThenBy(x => x.IngredientId).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct),
            TotalCount = await query.CountAsync(ct),
            Page = page,
            PageSize = pageSize
        };
    }
}
