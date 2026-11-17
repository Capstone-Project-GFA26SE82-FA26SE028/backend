using Identity.BLL.Interfaces;
using Identity.DAL.Interfaces;
using Shared.Common.Notifications;

namespace Identity.BLL.Implements;

public sealed class NotificationAccess(IUserRepository users, INotificationRestaurantAccess restaurants) : INotificationAccess
{
    public async Task<NotificationActor> GetActor(Guid userId, CancellationToken ct)
    {
        var user = await users.FindUserAsync(userId, ct);
        if (user is null || !user.IsActive || user.ProvisioningPending)
        {
            throw new NotificationException(401, "Phiên tài khoản không hợp lệ.");
        }

        var restaurantId = user.RoleId != "customer" && user.RestaurantId.HasValue
            && await restaurants.HasAssignmentAsync(userId, user.RestaurantId.Value, ct) ? user.RestaurantId : null;
        return new NotificationActor(userId, user.RoleId == "customer", restaurantId);
    }

    public async Task RequireAdmin(Guid userId, Guid restaurantId, CancellationToken ct)
    {
        var actor = await GetActor(userId, ct);
        var user = await users.FindUserAsync(userId, ct);
        if (restaurantId == Guid.Empty || user?.RoleId != "admin" || actor.RestaurantId != restaurantId)
        {
            throw new NotificationException(403, "Chỉ admin của nhà hàng được quản lý thông báo.");
        }
    }
}
