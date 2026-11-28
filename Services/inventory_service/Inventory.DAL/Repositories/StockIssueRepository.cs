using Inventory.DAL.Models;
using Microsoft.EntityFrameworkCore;

namespace Inventory.DAL.Repositories;

public sealed class StockIssueRepository(InventoryDbContext db)
{
    public Task<StockIssue?> GetByIdAsync(Guid id, CancellationToken ct)
    {
        return db.Set<StockIssue>()
            .Include(x => x.Movements)
                .ThenInclude(m => m.GoodsReceiptItem)
                    .ThenInclude(item => item.GoodsReceipt)
            .SingleOrDefaultAsync(x => x.StockIssueId == id, ct);
    }

    public Guid? GetRestaurantId(StockIssue issue)
    {
        return db.Entry(issue).Property<Guid?>("RestaurantId").CurrentValue;
    }

    public async Task CreateAsync(StockIssue issue, CancellationToken ct)
    {
        await db.Set<StockIssue>().AddAsync(issue, ct);
    }
}
