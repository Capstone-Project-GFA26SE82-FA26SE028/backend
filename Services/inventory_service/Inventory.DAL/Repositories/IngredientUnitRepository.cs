using Inventory.DAL.Models;
using Microsoft.EntityFrameworkCore;

namespace Inventory.DAL.Repositories;

public sealed class IngredientUnitRepository(InventoryDbContext db)
{
    public Task<IngredientUnit?> GetByIdAsync(Guid ingredientId, Guid unitId, CancellationToken ct)
    {
        return db.IngredientUnits.SingleOrDefaultAsync(x => x.IngredientId == ingredientId && x.UnitId == unitId, ct);
    }

    public Task<List<IngredientUnit>> GetAllAsync(Guid ingredientId, CancellationToken ct)
    {
        return db.IngredientUnits.AsNoTracking().Where(x => x.IngredientId == ingredientId).OrderBy(x => x.UnitId).ToListAsync(ct);
    }

    public async Task CreateAsync(IngredientUnit conversion, CancellationToken ct)
    {
        await db.IngredientUnits.AddAsync(conversion, ct);
    }
}
