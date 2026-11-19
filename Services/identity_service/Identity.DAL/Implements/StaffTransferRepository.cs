using Identity.DAL.Interfaces;
using Identity.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace Identity.DAL.Implements;

public sealed class StaffTransferRepository(IdentityDbContext db) : IStaffTransferRepository
{
    public async Task<StaffTransfer?> GetByIdAsync(Guid id, CancellationToken ct)
    {
        var tracked = db.Set<StaffTransfer>().Local.FirstOrDefault(item => item.StaffTransferId == id);
        if (tracked is not null)
        {
            await db.Entry(tracked).ReloadAsync(ct);
            return db.Entry(tracked).State == EntityState.Detached ? null : tracked;
        }

        return await db.Set<StaffTransfer>().SingleOrDefaultAsync(item => item.StaffTransferId == id, ct);
    }

    public Task<bool> HasPendingAsync(Guid userId, CancellationToken ct)
    {
        return db.Set<StaffTransfer>().AnyAsync(item => item.UserId == userId && item.Status == "Pending", ct);
    }

    public Task<List<Guid>> GetPendingAsync(CancellationToken ct)
    {
        return db.Set<StaffTransfer>().Where(item => item.Status == "Pending")
            .OrderBy(item => item.CreatedAt).Select(item => item.StaffTransferId).Take(50).ToListAsync(ct);
    }

    public void Create(StaffTransfer transfer)
    {
        db.Set<StaffTransfer>().Add(transfer);
    }
}
