using Restaurant.BLL.Interfaces;
using Restaurant.Common.Exceptions;
using Restaurant.DAL.Interfaces;

namespace Restaurant.BLL.Implements;

public sealed class RestaurantDataScopeService(IStaffIdentity identity, IRestaurantStaffRepository staff,
    IRestaurantRepository restaurants, ITableSessionRepository sessions, IGuestAccessService guests)
    : IRestaurantDataScopeService
{
    public async Task<Guid> Resolve(Guid? restaurantId, Guid? sessionId, string guestToken, bool allowCustomer, CancellationToken ct)
    {
        if (sessionId.HasValue)
        {
            var session = await sessions.GetForBillingAsync(sessionId.Value, ct)
                ?? throw new ManagementException(404, "Không tìm thấy phiên bàn.");
            var owners = session.SessionTables.Select(link => link.Table.Area.RestaurantId).Distinct().ToArray();
            if (owners.Length != 1 || (restaurantId.HasValue && restaurantId != owners[0]))
            {
                throw new ManagementException(403, "Phiên bàn không thuộc nhà hàng được yêu cầu.");
            }

            restaurantId = owners[0];
            if (!string.IsNullOrEmpty(guestToken))
            {
                if (!allowCustomer)
                {
                    throw new ManagementException(403, "Khách không có quyền thực hiện thao tác quản trị.");
                }

                await guests.GetMe(sessionId.Value, guestToken, ct);
                return restaurantId.Value;
            }
        }
        else if (!string.IsNullOrEmpty(guestToken))
        {
            throw new ManagementException(401, "Yêu cầu phiên bàn hợp lệ.");
        }

        var actor = await identity.GetCurrentAsync(ct);
        var assigned = await staff.GetRestaurantIdsAsync(actor.UserId, ct);
        if (!restaurantId.HasValue)
        {
            if (assigned.Count != 1)
            {
                throw new ManagementException(400, "Vui lòng chọn một nhà hàng bằng X-Restaurant-Id.");
            }

            restaurantId = assigned.Single();
        }

        if (!actor.IsAdministrator && !(allowCustomer && actor.RoleId == "customer")
            && !assigned.Contains(restaurantId.Value))
        {
            throw new ManagementException(403, "Bạn không được phân công tại nhà hàng này.");
        }

        if (await restaurants.GetByIdAsync(restaurantId.Value, ct) is null)
        {
            throw new ManagementException(404, "Không tìm thấy nhà hàng.");
        }

        return restaurantId.Value;
    }
}
