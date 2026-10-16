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
            source = source.Where(x => x.RestaurantName.Contains(query.Search.Trim()));
        }

        var count = await source.CountAsync(ct);
        var items = await source.OrderBy(x => x.RestaurantName).ThenBy(x => x.RestaurantId)
            .Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToListAsync(ct);
        return new PageResult<global::Restaurant.DAL.Models.Restaurant>
        {
            Items = items,
            TotalCount = count,
            Page = query.Page,
            PageSize = query.PageSize
        };
    }

    public override IQueryable<Entity> GetAll()
    {
        return _context.Restaurants.Include(x => x.OpeningHours);
    }

    public override Task<Entity?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return GetAll().SingleOrDefaultAsync(x => x.RestaurantId == id, ct);
    }

    public void ReplaceOpeningHours(Entity entity, IEnumerable<OpeningHour> hours)
    {
        _context.OpeningHours.RemoveRange(entity.OpeningHours);
        entity.OpeningHours = hours.ToList();
    }
}
