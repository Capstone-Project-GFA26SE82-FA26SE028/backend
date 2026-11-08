using System.Data;
using Billing.Common.DTOs;
using Billing.DAL.Interfaces;
using Billing.DAL.Models;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Shared.Common.Pagination;
using Shared.Persistence.Repositories;

namespace Billing.DAL.Implements;

public sealed class CashRegisterRepository(BillingDbContext db)
    : GenericRepository<CashRegister, BillingDbContext>(db), ICashRegisterRepository
{
    public Task<CashRegister?> LockByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        if (_context.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("Cần có giao dịch đang hoạt động để khóa quầy thu ngân.");
        }

        var source = _context.Database.IsSqlServer()
            ? _context.CashRegisters.FromSqlInterpolated($"SELECT * FROM dbo.CashRegister WITH (UPDLOCK, HOLDLOCK) WHERE CashRegisterId = {id}")
            : _context.CashRegisters.Where(register => register.CashRegisterId == id);
        return source.SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<PagedResult<CashRegister>> GetPageAsync(CashRegisterQueryDTO query,
        IReadOnlyList<Guid>? restaurantIds, CancellationToken cancellationToken)
    {
        var source = _context.CashRegisters.AsNoTracking();
        if (restaurantIds is not null)
        {
            source = source.Where(register => restaurantIds.Contains(register.RestaurantId));
        }

        if (query.RestaurantId.HasValue)
        {
            source = source.Where(register => register.RestaurantId == query.RestaurantId.Value);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim().ToUpperInvariant();
            source = source.Where(register => register.Code.ToUpper().Contains(search));
        }

        var totalCount = await source.CountAsync(cancellationToken);
        var items = await source.OrderBy(register => register.Code).ThenBy(register => register.CashRegisterId)
            .Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToListAsync(cancellationToken);
        return new PagedResult<CashRegister>(items, totalCount, query.Page, query.PageSize);
    }

    public Task<bool> CodeExistsAsync(Guid restaurantId, string code, CancellationToken cancellationToken)
    {
        var normalized = code.ToUpperInvariant();
        return _context.CashRegisters.AsNoTracking().AnyAsync(register => register.RestaurantId == restaurantId
            && register.Code.ToUpper() == normalized, cancellationToken);
    }

    public async Task<bool> LockRestaurantAsync(Guid restaurantId, CancellationToken cancellationToken)
    {
        if (_context.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("Cần có giao dịch đang hoạt động để khóa thao tác tạo quầy thu ngân.");
        }

        if (!_context.Database.IsSqlServer())
        {
            return true;
        }

        var resource = new SqlParameter("@resource", SqlDbType.NVarChar, 255)
        {
            Value = $"Billing.CashRegister:{restaurantId:N}"
        };
        var result = new SqlParameter("@result", SqlDbType.Int)
        {
            Direction = ParameterDirection.Output
        };
        await _context.Database.ExecuteSqlRawAsync(
            "EXEC @result = sys.sp_getapplock @Resource = @resource, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 10000;",
            [result, resource], cancellationToken);
        return (int)result.Value >= 0;
    }
}
