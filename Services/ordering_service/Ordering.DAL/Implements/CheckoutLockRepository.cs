using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Ordering.DAL.Interfaces;
using Ordering.DAL.Models;
using Ordering.Common.Exceptions;

namespace Ordering.DAL.Implements;

public sealed class CheckoutLockRepository(OrderingDbContext db) : ICheckoutLockRepository
{
    public async Task LockSessionAsync(Guid sessionId, CancellationToken ct)
    {
        if (db.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("Checkout requires an active transaction.");
        }

        if (!db.Database.IsSqlServer())
        {
            return;
        }

        var resource = new SqlParameter("@resource", $"Ordering.Checkout.Session:{sessionId:N}");
        var result = new SqlParameter("@result", SqlDbType.Int)
        {
            Direction = ParameterDirection.Output
        };
        await db.Database.ExecuteSqlRawAsync(
            "EXEC @result=sys.sp_getapplock @Resource=@resource,@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=10000;",
            [result, resource], ct);
        if ((int)result.Value < 0)
        {
            throw new OrderingException(409, "The session is busy. Retry the same request.");
        }
    }

    public Task<CheckoutLock?> GetActiveAsync(Guid sessionId, CancellationToken ct)
    {
        return db.CheckoutLocks.SingleOrDefaultAsync(value => value.TableSessionRef == sessionId && value.ReleasedAt == null, ct);
    }

    public Task<CheckoutLock?> GetByIdAsync(Guid id, CancellationToken ct)
    {
        return db.CheckoutLocks.SingleOrDefaultAsync(value => value.CheckoutId == id, ct);
    }

    public void Add(CheckoutLock value)
    {
        db.CheckoutLocks.Add(value);
    }
}
