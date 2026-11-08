using Restaurant.Common.DTOs;

namespace Restaurant.BLL.Interfaces;

public interface ICurrentRestaurantScopeService
{
    Task<CurrentRestaurantScopeDto> GetCurrentScope(Guid? restaurantId, CancellationToken ct);
}
