using Shared.Persistence.Repositories;
using Restaurant.Common.DTOs;
using Restaurant.DAL.Interfaces;
using Microsoft.EntityFrameworkCore;
using Restaurant.DAL.Models;
using TableEntity = Restaurant.DAL.Models.Table;

namespace Restaurant.DAL.Implements;

public sealed class TableRepository(RestaurantDbContext db) : GenericRepository<global::Restaurant.DAL.Models.Table, RestaurantDbContext>(db), ITableRepository
{
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
