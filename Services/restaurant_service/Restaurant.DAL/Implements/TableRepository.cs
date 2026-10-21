using Shared.Persistence.Repositories;
using Restaurant.Common.DTOs;
using Restaurant.DAL.Interfaces;
using Microsoft.EntityFrameworkCore;
using Restaurant.DAL.Models;
using TableEntity = Restaurant.DAL.Models.Table;

namespace Restaurant.DAL.Implements;

public sealed class TableRepository(RestaurantDbContext db) : GenericRepository<global::Restaurant.DAL.Models.Table, RestaurantDbContext>(db), ITableRepository
{
    public async Task<PageResult<TableEntity>> GetServicePageAsync(ListQuery query, CancellationToken ct)
    {
        var source = _context.Tables.AsNoTracking().Where(table => table.IsActive && table.Area.IsActive);
        if (query.RestaurantId.HasValue)
        {
            source = source.Where(table => table.Area.RestaurantId == query.RestaurantId.Value);
        }

        if (query.AreaId.HasValue)
        {
            source = source.Where(table => table.AreaId == query.AreaId.Value);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            source = source.Where(table => table.TableCode.Contains(query.Search.Trim()));
        }

        var count = await source.CountAsync(ct);
        var tables = await source.Include(table => table.Area)
            .Include(table => table.SessionTables.Where(link => link.ReleasedAt == null
                && link.TableSession.Status == "Open" && link.TableSession.ClosedAt == null))
            .OrderBy(table => table.TableCode).ThenBy(table => table.TableId)
            .Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).AsSplitQuery().ToListAsync(ct);
        return new PageResult<TableEntity>
        {
            Items = tables,
            TotalCount = count,
            Page = query.Page,
            PageSize = query.PageSize
        };
    }

    public Task<TableEntity?> LockForSessionAsync(Guid id, CancellationToken ct)
    {
        if (_context.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("Opening a table session requires a transaction.");
        }

        if (_context.Database.IsSqlServer())
        {
            // Serialize scans for this table across processes until the transaction commits.
            return _context.Tables.FromSqlInterpolated(
                $"SELECT * FROM [Table] WITH (UPDLOCK, HOLDLOCK) WHERE [TableId] = {id}")
                .SingleOrDefaultAsync(ct);
        }

        return GetByIdAsync(id, ct);
    }

    public async Task<PageResult<global::Restaurant.DAL.Models.Table>> GetPageAsync(ListQuery query, CancellationToken ct)
    {
        var source = GetAll().AsNoTracking();
        if (query.RestaurantId.HasValue)
        {
            source = source.Where(x => x.Area.RestaurantId == query.RestaurantId.Value);
        }

        if (query.AreaId.HasValue)
        {
            source = source.Where(x => x.AreaId == query.AreaId.Value);
        }

        if (query.IsActive.HasValue)
        {
            source = source.Where(x => x.IsActive == query.IsActive.Value);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            source = source.Where(x => x.TableCode.Contains(query.Search.Trim()));
        }

        var count = await source.CountAsync(ct);
        var items = await source.OrderBy(x => x.TableCode).ThenBy(x => x.TableId)
            .Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToListAsync(ct);
        return new PageResult<global::Restaurant.DAL.Models.Table>
        {
            Items = items,
            TotalCount = count,
            Page = query.Page,
            PageSize = query.PageSize
        };
    }

    public override IQueryable<TableEntity> GetAll()
    {
        return _context.Tables.Include(x => x.Area);
    }

    public override Task<TableEntity?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return GetAll().SingleOrDefaultAsync(x => x.TableId == id, ct);
    }

    public Task<bool> CodeExistsAsync(Guid restaurantId, string code, Guid exceptId, CancellationToken ct)
    {
        return _context.Tables.AnyAsync(x => x.Area.RestaurantId == restaurantId && x.TableId != exceptId
            && x.TableCode.ToUpper() == code.ToUpper(), ct);
    }

    public Task<bool> HasOpenSessionAsync(Guid id, CancellationToken ct)
    {
        return _context.SessionTables.AnyAsync(x => x.TableId == id && x.TableSession.ClosedAt == null, ct);
    }

    public Task<bool> HasHistoryAsync(Guid id, CancellationToken ct)
    {
        return _context.Tables.AnyAsync(x => x.TableId == id
            && (x.SessionTables.Any() || x.Qrs.Any() || x.Reservations.Any()), ct);
    }
}
