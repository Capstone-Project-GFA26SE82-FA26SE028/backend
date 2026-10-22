using Restaurant.Common.DTOs;

namespace Restaurant.BLL.Interfaces;

public interface IRestaurantStaffService
{
    Task<RestaurantStaffDto> Create(Guid restaurantId, Guid userId, CancellationToken ct);
    Task<IReadOnlyList<RestaurantStaffDto>> GetAll(Guid restaurantId, CancellationToken ct);
    Task<RestaurantStaffDto> Delete(Guid restaurantId, Guid userId, CancellationToken ct);
}
