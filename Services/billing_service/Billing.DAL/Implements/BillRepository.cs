using System.Data;
using Billing.Common.DTOs;
using Billing.DAL.Interfaces;
using Billing.DAL.Models;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Shared.Common.Pagination;
using Shared.Persistence.Repositories;

namespace Billing.DAL.Implements;

public sealed class BillRepository(BillingDbContext db) : GenericRepository<Bill, BillingDbContext>(db), IBillRepository
{
    public async Task<bool> LockSessionAsync(Guid sessionId, CancellationToken ct)
    {
        if (_context.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("An active transaction is required to lock bill creation.");
        }

        if (!_context.Database.IsSqlServer())
        {
            return true;
        }

        var resource = new SqlParameter("@resource", SqlDbType.NVarChar, 255)
        {
            Value = $"Billing.Bill.Session:{sessionId:N}"
        };
        var result = new SqlParameter("@result", SqlDbType.Int)
        {
            Direction = ParameterDirection.Output
        };
        await _context.Database.ExecuteSqlRawAsync(
            "EXEC @result = sys.sp_getapplock @Resource = @resource, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 10000;",
            [result, resource], ct);
        return (int)result.Value >= 0;
    }

    public Task<bool> SessionHasBillAsync(Guid sessionId, CancellationToken ct)
    {
        return _context.BillLines.AnyAsync(line => line.TableSessionRef == sessionId, ct);
    }

    public Task<Bill?> GetDetailsAsync(Guid id, CancellationToken ct)
    {
        return _context.Bills.AsNoTracking().Include(bill => bill.BillLines)
            .SingleOrDefaultAsync(bill => bill.BillId == id, ct);
    }

    public Task<Bill?> GetForRefreshAsync(Guid id, CancellationToken ct)
    {
        if (_context.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("An active transaction is required to lock a bill for refresh.");
        }

        var query = _context.Database.IsSqlServer()
            ? _context.Bills.FromSqlInterpolated($"SELECT * FROM dbo.Bill WITH (UPDLOCK, HOLDLOCK) WHERE BillId = {id}")
            : _context.Bills.Where(bill => bill.BillId == id);
        return query.Include(bill => bill.BillLines)
            .Include(bill => bill.Payments)
            .Include(bill => bill.BillAdjustments)
            .Include(bill => bill.VoucherRedemptions)
            .SingleOrDefaultAsync(ct);
    }

    public async Task<PagedResult<Bill>> GetPageAsync(BillQueryDTO query, IReadOnlyList<Guid>? restaurantIds, CancellationToken ct)
    {
        var source = _context.Bills.AsNoTracking();
        if (restaurantIds is not null)
        {
            source = source.Where(bill => restaurantIds.Contains(bill.RestaurantId));
        }

        if (query.RestaurantId.HasValue)
        {
            source = source.Where(bill => bill.RestaurantId == query.RestaurantId.Value);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim();
            source = source.Where(bill => bill.BillNumber.Contains(search));
        }

        var count = await source.CountAsync(ct);
        var items = await source.Include(bill => bill.BillLines).OrderBy(bill => bill.BillNumber).ThenBy(bill => bill.BillId)
            .Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToListAsync(ct);
        return new PagedResult<Bill>(items, count, query.Page, query.PageSize);
    }
}
