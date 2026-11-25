using Restaurant.Common.DTOs;

namespace Restaurant.BLL.Interfaces;

public interface ICurrentRestaurantScopeService
{
    Task<RestaurantReportContextDto> GetReportContext(Guid restaurantId, CancellationToken ct);

    Task<CurrentRestaurantScopeDto> GetCurrentScope(Guid? restaurantId, CancellationToken ct);
}
