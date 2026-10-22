using Inventory.Common.DTOs;
using Inventory.DAL.Models;
using Microsoft.EntityFrameworkCore;

namespace Inventory.DAL.Repositories;

public sealed class GoodsReceiptRepository(InventoryDbContext db)
{
    public Task<GoodsReceipt?> GetByIdAsync(Guid id, CancellationToken ct)
    {
        return db.GoodsReceipts.Include(x => x.GoodsReceiptItems).SingleOrDefaultAsync(x => x.GoodsReceiptId == id, ct);
    }

    public async Task CreateAsync(GoodsReceipt receipt, CancellationToken ct)
    {
        await db.GoodsReceipts.AddAsync(receipt, ct);
    }

    public async Task<PageDTO<GoodsReceipt>> GetAllAsync(int page, int pageSize, CancellationToken ct)
    {
        return new PageDTO<GoodsReceipt>
        {
            Items = await db.GoodsReceipts.AsNoTracking().Include(x => x.GoodsReceiptItems)
                .OrderByDescending(x => x.CreatedAt).ThenBy(x => x.GoodsReceiptId)
                .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct),
            TotalCount = await db.GoodsReceipts.CountAsync(ct),
            Page = page,
            PageSize = pageSize
        };
    }
}
