using Shared.Persistence.Interfaces;
using Restaurant.Common.DTOs;
using Restaurant.DAL.Models;
using Entity = Restaurant.DAL.Models.Restaurant;

namespace Restaurant.DAL.Interfaces;

public interface IRestaurantRepository : IGenericRepository<global::Restaurant.DAL.Models.Restaurant>
{
    Task<PageResult<global::Restaurant.DAL.Models.Restaurant>> GetPageAsync(ListQuery query, CancellationToken ct);

    void ReplaceOpeningHours(Entity entity, IEnumerable<OpeningHour> hours);
}
