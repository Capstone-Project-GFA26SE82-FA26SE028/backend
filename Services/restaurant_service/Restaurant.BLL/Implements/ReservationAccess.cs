using Microsoft.Extensions.Options;
using Restaurant.BLL.Interfaces;
using Restaurant.Common.Exceptions;
using Restaurant.Common.Settings;
using Restaurant.DAL.Interfaces;

namespace Restaurant.BLL.Implements;

public sealed class ReservationAccess(IStaffIdentity identity, IOptions<ReservationSettings> settings,
    IRestaurantStaffRepository staff) : IReservationAccess
{
    public async Task<IReadOnlyList<Guid>?> GetReadScopeAsync(CancellationToken ct)
    {
        var actor = await identity.GetCurrentAsync(ct);
        if (actor.IsAdministrator)
        {
            return null;
        }

        RequireStaffRole(actor.RoleId);
        var restaurantIds = await staff.GetRestaurantIdsAsync(actor.UserId, ct);
        if (restaurantIds.Count == 0)
        {
            throw new ManagementException(403, "An active restaurant assignment is required.");
        }

        return restaurantIds;
    }

    public async Task RequireReadAsync(Guid restaurantId, CancellationToken ct)
    {
        var scope = await GetReadScopeAsync(ct);
        if (scope is not null && !scope.Contains(restaurantId))
        {
            throw new ManagementException(403, "You are not assigned to this restaurant.");
        }
    }

    public async Task<Guid> RequireMutationAsync(Guid restaurantId, CancellationToken ct)
    {
        var actor = await identity.GetCurrentAsync(ct);
        if (actor.IsAdministrator)
        {
            throw new ManagementException(403, "Administrator access does not grant permission to modify reservations.");
        }

        RequireStaffRole(actor.RoleId);
        if (!await staff.IsActiveAsync(restaurantId, actor.UserId, ct))
        {
            throw new ManagementException(403, "An active assignment to this restaurant is required.");
        }

        return actor.UserId;
    }

    private void RequireStaffRole(string roleId)
    {
        if (!settings.Value.StaffRoleIds.Contains(roleId, StringComparer.Ordinal))
        {
            throw new ManagementException(403, "Only configured Waiter/Cashier roles can perform this reservation operation.");
        }
    }
}
