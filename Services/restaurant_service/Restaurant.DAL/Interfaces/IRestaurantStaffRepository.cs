using Restaurant.DAL.Models;
using Shared.Persistence.Interfaces;

namespace Restaurant.DAL.Interfaces;

public interface IRestaurantStaffRepository : IGenericRepository<RestaurantStaff>
{
    Task<RestaurantStaff?> LockAsync(Guid restaurantId, Guid userId, CancellationToken ct);
    Task<bool> IsActiveAsync(Guid restaurantId, Guid userId, CancellationToken ct);
    Task<IReadOnlyList<Guid>> GetRestaurantIdsAsync(Guid userId, CancellationToken ct);
    Task<IReadOnlyList<RestaurantStaff>> GetAllAsync(Guid restaurantId, CancellationToken ct);
}
