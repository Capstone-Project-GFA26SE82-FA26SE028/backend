using Inventory.DAL.Models;
using Microsoft.EntityFrameworkCore;

namespace Inventory.DAL.Repositories;

public sealed class UnitRepository(InventoryDbContext db)
{
    public Task<Unit?> GetByIdAsync(Guid id, CancellationToken ct)
    {
        return db.Units.SingleOrDefaultAsync(x => x.UnitId == id, ct);
    }

    public Task<Unit?> GetByCodeAsync(string code, CancellationToken ct)
    {
        return db.Units.SingleOrDefaultAsync(x => x.Code == code, ct);
    }

    public Task<List<Unit>> GetAllAsync(CancellationToken ct)
    {
        return db.Units.AsNoTracking().OrderBy(x => x.Code).ToListAsync(ct);
    }

    public async Task CreateAsync(Unit unit, CancellationToken ct)
    {
        await db.Units.AddAsync(unit, ct);
    }
}
