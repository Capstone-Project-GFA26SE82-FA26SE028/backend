using System.Data;
using System.Security.Cryptography;
using System.Text;
using Billing.DAL.Interfaces;
using Billing.DAL.Models;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Shared.Common.Pagination;
using Shared.Persistence.Repositories;

namespace Billing.DAL.Implements;

public sealed class CashMovementRepository(BillingDbContext db)
    : GenericRepository<CashMovement, BillingDbContext>(db), ICashMovementRepository
{
    public async Task<bool> LockIdempotencyKeyAsync(string key, CancellationToken ct)
    {
        if (_context.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("Cần có giao dịch đang hoạt động để khóa bút toán tiền mặt.");
        }

        if (!_context.Database.IsSqlServer())
        {
            return true;
        }

        var resource = new SqlParameter("@resource", SqlDbType.NVarChar, 255)
        {
            Value = "Billing.CashMovement.Key:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)))
        };
        var result = new SqlParameter("@result", SqlDbType.Int)
        {
            Direction = ParameterDirection.Output
        };
        await _context.Database.ExecuteSqlRawAsync(
            "EXEC @result=sys.sp_getapplock @Resource=@resource,@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=10000;",
            [result, resource], ct);
        return (int)result.Value >= 0;
    }

    public async Task<IReadOnlyList<CashMovement>> GetByKeyAsync(string key, CancellationToken ct)
    {
        return await Details().Where(value => value.IdempotencyKey == key).Take(2).ToListAsync(ct);
    }

    public Task<CashMovement?> GetDetailsAsync(Guid id, CancellationToken ct)
    {
        return Details().SingleOrDefaultAsync(value => value.CashMovementId == id, ct);
    }

    public async Task<decimal> GetTotalAsync(Guid shiftId, CancellationToken ct)
    {
        var source = _context.CashMovements.AsNoTracking().Where(value => value.CashShiftId == shiftId);
        if (!_context.Database.IsSqlServer())
        {
            return (await source.Select(value => value.SignedAmount).ToListAsync(ct)).Sum();
        }

        return await source.SumAsync(value => (decimal?)value.SignedAmount, ct) ?? 0m;
    }

    public async Task<PagedResult<CashMovement>> GetPageAsync(Guid shiftId, int page, int pageSize, CancellationToken ct)
    {
        var source = Details().Where(value => value.CashShiftId == shiftId);
        var count = await source.CountAsync(ct);
        var items = await source.OrderByDescending(value => value.PostedAt).ThenByDescending(value => value.CashMovementId)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return new PagedResult<CashMovement>(items, count, page, pageSize);
    }

    public async Task<IReadOnlyList<CashMovement>> GetForShiftAsync(Guid shiftId, CancellationToken ct)
    {
        return await _context.CashMovements.AsNoTracking().Where(value => value.CashShiftId == shiftId).ToListAsync(ct);
    }

    private IQueryable<CashMovement> Details()
    {
        return _context.CashMovements.AsNoTracking().Include(value => value.CashShift).ThenInclude(shift => shift.CashRegister)
            .Include(value => value.Payment);
    }

    public Task<bool> PaymentHasMovementAsync(Guid paymentId, CancellationToken ct)
    {
        return _context.CashMovements.AnyAsync(movement => movement.PaymentId == paymentId, ct);
    }
}
