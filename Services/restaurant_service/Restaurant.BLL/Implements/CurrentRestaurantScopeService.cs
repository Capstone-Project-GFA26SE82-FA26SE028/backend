using Restaurant.BLL.Interfaces;
using Restaurant.Common.DTOs;
using Restaurant.Common.Exceptions;
using Restaurant.DAL.Interfaces;

namespace Restaurant.BLL.Implements;

public sealed class CurrentRestaurantScopeService(IStaffIdentity identity,
    IRestaurantStaffRepository staff, IRestaurantRepository restaurants) : ICurrentRestaurantScopeService
{
    public async Task<CurrentRestaurantScopeDto> GetCurrentScope(Guid? restaurantId, CancellationToken ct)
    {
        var actor = await identity.GetCurrentAsync(ct);
        if (restaurantId == Guid.Empty)
        {
            throw new ManagementException(400, "A valid restaurant ID is required.");
        }

        var restaurantIds = actor.IsAdministrator
            ? Array.Empty<Guid>()
            : await staff.GetRestaurantIdsAsync(actor.UserId, ct);
        if (restaurantId.HasValue)
        {
            if (!actor.IsAdministrator && !restaurantIds.Contains(restaurantId.Value))
            {
                throw new ManagementException(403, "An active assignment to this restaurant is required.");
            }

            if (await restaurants.GetByIdAsync(restaurantId.Value, ct) is null)
            {
                throw new ManagementException(404, "Restaurant not found.");
            }
        }

        return new CurrentRestaurantScopeDto
        {
            UserId = actor.UserId,
            RoleId = actor.RoleId,
            IsAdministrator = actor.IsAdministrator,
            RestaurantIds = restaurantIds.OrderBy(value => value).ToArray()
        };
    }
}
