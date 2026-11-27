using Shared.Persistence.Repositories;
using Restaurant.Common.DTOs;
using Restaurant.DAL.Interfaces;
using Microsoft.EntityFrameworkCore;
using Restaurant.DAL.Models;
using Entity = Restaurant.DAL.Models.Restaurant;

namespace Restaurant.DAL.Implements;

public sealed class RestaurantRepository(RestaurantDbContext db) : GenericRepository<global::Restaurant.DAL.Models.Restaurant, RestaurantDbContext>(db), IRestaurantRepository
{
    public async Task<PageResult<global::Restaurant.DAL.Models.Restaurant>> GetPageAsync(ListQuery query, CancellationToken ct)
    {
        var source = GetAll().AsNoTracking();
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            source = source.Where(x => x.RestaurantName.Contains(query.Search.Trim()) || x.Address.Contains(query.Search.Trim()));
        }

        var count = await source.CountAsync(ct);
        var items = await source.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.RestaurantId)
            .Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToListAsync(ct);
        return new PageResult<global::Restaurant.DAL.Models.Restaurant>
        {
            Items = items,
            TotalCount = count,
            Page = query.Page,
            PageSize = query.PageSize
        };
    }

    public Task<Dictionary<Guid, string>> GetNamesAsync(Guid[] ids, CancellationToken ct)
    {
        return _context.Restaurants.AsNoTracking().Where(item => ids.Contains(item.RestaurantId))
            .ToDictionaryAsync(item => item.RestaurantId, item => item.RestaurantName, ct);
    }

    public override IQueryable<Entity> GetAll()
    {
        return _context.Restaurants.Include(x => x.OpeningHours);
    }

    public override Task<Entity?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return GetAll().SingleOrDefaultAsync(x => x.RestaurantId == id, ct);
    }

    public async Task<Entity?> LockAsync(Guid id, CancellationToken ct)
    {
        if (_context.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("Cần giao dịch để cập nhật logo nhà hàng.");
        }

        var source = _context.Database.IsSqlServer()
            ? _context.Restaurants.FromSqlInterpolated($"SELECT * FROM dbo.Restaurant WITH (UPDLOCK, HOLDLOCK) WHERE RestaurantId = {id}")
            : _context.Restaurants.Where(item => item.RestaurantId == id);
        var item = await source.SingleOrDefaultAsync(ct);
        if (item is null)
        {
            return null;
        }

        await _context.Entry(item).ReloadAsync(ct);
        return _context.Entry(item).State == EntityState.Detached ? null : item;
    }

    public void ReplaceOpeningHours(Entity entity, IEnumerable<OpeningHour> hours)
    {
        _context.OpeningHours.RemoveRange(entity.OpeningHours);
        entity.OpeningHours = hours.ToList();
    }
}
