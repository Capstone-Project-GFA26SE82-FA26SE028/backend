using Inventory.DAL.Models;
using Microsoft.EntityFrameworkCore;

namespace Inventory.DAL.Repositories;

public sealed class StockIssueRepository(InventoryDbContext db)
{
    public Task<StockIssue?> GetByIdAsync(Guid id, CancellationToken ct)
    {
        return db.Set<StockIssue>().Include(x => x.Movements).SingleOrDefaultAsync(x => x.StockIssueId == id, ct);
    }

    public async Task CreateAsync(StockIssue issue, CancellationToken ct)
    {
        await db.Set<StockIssue>().AddAsync(issue, ct);
    }
}
