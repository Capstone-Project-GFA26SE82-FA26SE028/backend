using Restaurant.BLL.Interfaces;
using Restaurant.Common.Exceptions;
using Restaurant.DAL.Interfaces;

namespace Restaurant.BLL.Implements;

public sealed class LayoutManagementAccess(IStaffIdentity identity, IRestaurantStaffRepository staff)
    : ILayoutManagementAccess
{
    public async Task RequireManager(CancellationToken ct)
    {
        var actor = await identity.GetCurrentAsync(ct);
        if (actor.UserId == Guid.Empty || actor.IsAdministrator || actor.RoleId is not ("admin" or "manager"))
        {
            throw new ManagementException(403, "Yêu cầu quyền admin hoặc quản lý của nhà hàng.");
        }
    }

    public async Task<Guid> RequireRestaurant(Guid? restaurantId, CancellationToken ct)
    {
        await RequireManager(ct);
        var actor = await identity.GetCurrentAsync(ct);
        var assigned = (await staff.GetRestaurantIdsAsync(actor.UserId, ct))
            .Where(id => !actor.RestaurantId.HasValue || id == actor.RestaurantId.Value).ToArray();
        if (!restaurantId.HasValue)
        {
            if (assigned.Length != 1)
            {
                throw new ManagementException(400, "Vui lòng chọn một nhà hàng được phân công.");
            }

            restaurantId = assigned.Single();
        }

        if (!assigned.Contains(restaurantId.Value))
        {
            throw new ManagementException(403, "Bạn không được phân công tại nhà hàng này.");
        }

        return restaurantId.Value;
    }
}
