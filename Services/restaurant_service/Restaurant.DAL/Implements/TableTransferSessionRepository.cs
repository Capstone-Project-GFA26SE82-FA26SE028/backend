using Microsoft.EntityFrameworkCore;
using Restaurant.DAL.Interfaces;
using Restaurant.DAL.Models;
using Restaurant.Common.Exceptions;

namespace Restaurant.DAL.Implements;

public sealed class TableTransferSessionRepository(RestaurantDbContext db) : ITableTransferSessionRepository
{
    public async Task<TableTransferSession?> GetAsync(Guid id, CancellationToken ct)
    {
        var tracked = db.TableTransfers.Local.SingleOrDefault(value => value.OperationId == id);
        if (tracked is not null)
        {
            await db.Entry(tracked).ReloadAsync(ct);
            return tracked;
        }
        return await db.TableTransfers.SingleOrDefaultAsync(value => value.OperationId == id, ct);
    }

    public async Task LockAsync(Guid id, CancellationToken ct)
    {
        if (db.Database.IsSqlServer())
        {
            var resource = $"Restaurant.TableTransfer:{id:N}";
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"DECLARE @r int; EXEC @r=sys.sp_getapplock @Resource={resource},@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=10000; IF @r < 0 THROW 51000, 'Table transfer is busy', 1;", ct);
        }
    }

    public void Add(TableTransferSession value)
    {
        db.TableTransfers.Add(value);
    }

    public async Task RevokeGuestsAsync(Guid sessionId, DateTime now, CancellationToken ct)
    {
        foreach (var guest in await db.GuestAccesses.Where(value => value.TableSessionId == sessionId && value.RevokedAt == null).ToListAsync(ct))
        {
            guest.RevokedAt = now;
        }
    }
}
