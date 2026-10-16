using Shared.Persistence.Repositories;
using Restaurant.Common.DTOs;
using Restaurant.DAL.Interfaces;
using Microsoft.EntityFrameworkCore;
using Restaurant.DAL.Models;

namespace Restaurant.DAL.Implements;

public sealed class QrRepository(RestaurantDbContext db) : GenericRepository<global::Restaurant.DAL.Models.Qr, RestaurantDbContext>(db), IQrRepository
{
    public async Task<PageResult<global::Restaurant.DAL.Models.Qr>> GetPageAsync(ListQuery query, CancellationToken ct)
    {
        var source = GetAll().AsNoTracking();
        if (query.RestaurantId.HasValue)
        {
            source = source.Where(x => x.Table.Area.RestaurantId == query.RestaurantId.Value);
        }

        if (query.AreaId.HasValue)
        {
            source = source.Where(x => x.Table.AreaId == query.AreaId.Value);
        }

        if (query.IsActive.HasValue)
        {
            source = source.Where(x => (x.RevokedAt == null) == query.IsActive.Value);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            source = source.Where(x => x.Table.TableCode.Contains(query.Search.Trim()));
        }

        var count = await source.CountAsync(ct);
        var items = await source.OrderByDescending(x => x.CreatedAt).ThenBy(x => x.QrId)
            .Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToListAsync(ct);
        return new PageResult<global::Restaurant.DAL.Models.Qr>
        {
            Items = items,
            TotalCount = count,
            Page = query.Page,
            PageSize = query.PageSize
        };
    }

    public override IQueryable<Qr> GetAll()
    {
        return _context.Qrs.Include(x => x.Table).ThenInclude(x => x.Area);
    }

    public Task<List<Qr>> GetActiveAsync(Guid tableId, CancellationToken ct)
    {
        return GetAll().Where(x => x.TableId == tableId && x.RevokedAt == null).ToListAsync(ct);
    }

    public Task<Qr?> FindByHashAsync(string hash, CancellationToken ct)
    {
        return GetAll().AsNoTracking().SingleOrDefaultAsync(x => x.TokenHash == hash && x.RevokedAt == null, ct);
    }
}
