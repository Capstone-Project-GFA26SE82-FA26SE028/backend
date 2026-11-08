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
            throw new ManagementException(403, "Yêu cầu phân công nhà hàng đang hoạt động.");
        }

        return restaurantIds;
    }

    public async Task RequireReadAsync(Guid restaurantId, CancellationToken ct)
    {
        var scope = await GetReadScopeAsync(ct);
        if (scope is not null && !scope.Contains(restaurantId))
        {
            throw new ManagementException(403, "Bạn chưa được phân công tại nhà hàng này.");
        }
    }

    public async Task<Guid> RequireMutationAsync(Guid restaurantId, CancellationToken ct)
    {
        var actor = await identity.GetCurrentAsync(ct);
        if (actor.IsAdministrator)
        {
            throw new ManagementException(403, "Quyền quản trị viên không bao gồm quyền chỉnh sửa yêu cầu đặt bàn.");
        }

        RequireStaffRole(actor.RoleId);
        if (!await staff.IsActiveAsync(restaurantId, actor.UserId, ct))
        {
            throw new ManagementException(403, "Yêu cầu phân công đang hoạt động tại nhà hàng này.");
        }

        return actor.UserId;
    }

    private void RequireStaffRole(string roleId)
    {
        if (!settings.Value.StaffRoleIds.Contains(roleId, StringComparer.Ordinal))
        {
            throw new ManagementException(403, "Chỉ vai trò phục vụ hoặc thu ngân đã được cấu hình mới có thể thực hiện thao tác đặt bàn này.");
        }
    }
}
