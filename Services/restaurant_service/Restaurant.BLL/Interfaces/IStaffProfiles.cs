using Restaurant.Common.DTOs;

namespace Restaurant.BLL.Interfaces;

public interface IStaffProfiles
{
    Task FillAsync(Guid restaurantId, IReadOnlyList<RestaurantStaffDto> assignments, CancellationToken ct);
}
