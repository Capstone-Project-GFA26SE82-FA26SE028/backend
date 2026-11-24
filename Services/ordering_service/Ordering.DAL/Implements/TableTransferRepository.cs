using Microsoft.EntityFrameworkCore;
using Ordering.DAL.Interfaces;
using Ordering.DAL.Models;

namespace Ordering.DAL.Implements;

public sealed class TableTransferRepository(OrderingDbContext db) : ITableTransferRepository
{
    public void ResetTracking()
    {
        db.ChangeTracker.Clear();
    }

    public Task<TableTransfer?> GetAsync(Guid id, CancellationToken ct)
    {
        return db.TableTransfers.SingleOrDefaultAsync(value => value.OperationId == id, ct);
    }

    public async Task LockSessionsAsync(Guid source, Guid target, CancellationToken ct)
    {
        foreach (var id in new[] { source, target }.Order())
        {
            if (db.Database.IsSqlServer())
            {
                var resource = $"Ordering.Checkout.Session:{id:N}";
                await db.Database.ExecuteSqlInterpolatedAsync(
                    $"DECLARE @r int; EXEC @r=sys.sp_getapplock @Resource={resource},@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=10000; IF @r < 0 THROW 51000, 'Table transfer is busy', 1;", ct);
            }
        }
    }

    public Task<bool> HasPendingAsync(Guid source, Guid target, CancellationToken ct)
    {
        return db.TableTransfers.AnyAsync(value => value.Status == "Applied"
            && (value.SourceSessionId == source || value.SourceSessionId == target
                || value.TargetSessionId == source || value.TargetSessionId == target), ct);
    }

    public void Add(TableTransfer operation)
    {
        db.TableTransfers.Add(operation);
    }
}
