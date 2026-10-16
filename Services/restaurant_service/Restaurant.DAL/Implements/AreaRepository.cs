using Shared.Persistence.Repositories;
using Restaurant.Common.DTOs;
using Restaurant.DAL.Interfaces;
using Microsoft.EntityFrameworkCore;
using Restaurant.DAL.Models;

namespace Restaurant.DAL.Implements;

public sealed class AreaRepository(RestaurantDbContext db) : GenericRepository<global::Restaurant.DAL.Models.Area, RestaurantDbContext>(db), IAreaRepository
{
    public async Task<PageResult<global::Restaurant.DAL.Models.Area>> GetPageAsync(ListQuery query, CancellationToken ct)
    {
        var source = GetAll().AsNoTracking();
        if (query.RestaurantId.HasValue)
        {
            source = source.Where(x => x.RestaurantId == query.RestaurantId.Value);
        }

        if (query.IsActive.HasValue)
        {
            source = source.Where(x => x.IsActive == query.IsActive.Value);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            source = source.Where(x => x.AreaCode.Contains(query.Search.Trim()) || x.AreaName.Contains(query.Search.Trim()));
        }

        var count = await source.CountAsync(ct);
        var items = await source.OrderBy(x => x.AreaCode).ThenBy(x => x.AreaId)
            .Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToListAsync(ct);
        return new PageResult<global::Restaurant.DAL.Models.Area>
        {
            Items = items,
            TotalCount = count,
            Page = query.Page,
            PageSize = query.PageSize
        };
    }

    public override Task<Area?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return _context.Areas.SingleOrDefaultAsync(x => x.AreaId == id, ct);
    }

    public Task<bool> CodeExistsAsync(Guid restaurantId, string code, Guid exceptId, CancellationToken ct)
    {
        return _context.Areas.AnyAsync(x => x.RestaurantId == restaurantId && x.AreaId != exceptId
            && x.AreaCode.ToUpper() == code.ToUpper(), ct);
    }

    public Task<bool> HasTablesAsync(Guid id, CancellationToken ct)
    {
        return _context.Tables.AnyAsync(x => x.AreaId == id, ct);
    }

    public Task<bool> HasOpenSessionsAsync(Guid id, CancellationToken ct)
    {
        return _context.SessionTables.AnyAsync(x => x.Table.AreaId == id && x.TableSession.ClosedAt == null, ct);
    }
}
