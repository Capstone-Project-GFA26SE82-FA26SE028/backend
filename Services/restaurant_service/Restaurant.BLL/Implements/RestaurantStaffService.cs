using Microsoft.Extensions.Options;
using Restaurant.BLL.Interfaces;
using Restaurant.Common.DTOs;
using Restaurant.Common.Exceptions;
using Restaurant.Common.Settings;
using Restaurant.DAL.Interfaces;
using Restaurant.DAL.Models;

namespace Restaurant.BLL.Implements;

public sealed class RestaurantStaffService(IManagementAccess access, IStaffIdentity identity,
    IRestaurantStaffRepository staff, IRestaurantRepository restaurants, IReservationTransaction transaction,
    IOptions<RestaurantStaffSettings> settings, IStaffProfiles? profiles = null) : IRestaurantStaffService
{
    public async Task<IReadOnlyList<RestaurantStaffDto>> GetNotificationRecipients(Guid restaurantId, CancellationToken ct)
    {
        await RequireRestaurant(restaurantId, ct);
        return (await staff.GetAllAsync(restaurantId, ct)).Where(value => value.IsActive).Select(Map).ToArray();
    }

    public async Task<RestaurantStaffDto> Provision(Guid restaurantId, Guid userId, CancellationToken ct)
    {
        ValidateIds(restaurantId, userId);
        return await transaction.ExecuteAsync(async () =>
        {
            await RequireRestaurant(restaurantId, ct);
            if ((await staff.GetRestaurantIdsAsync(userId, ct)).Any(id => id != restaurantId))
            {
                throw new ManagementException(409, "Nhân viên đã thuộc nhà hàng khác.");
            }

            var assignment = await staff.LockAsync(restaurantId, userId, ct);
            if (assignment is null)
            {
                assignment = new RestaurantStaff
                {
                    RestaurantId = restaurantId,
                    UserIdRef = userId,
                    IsActive = true
                };
                await staff.AddAsync(assignment);
            }

            return Map(assignment);
        }, ct);
    }

    public async Task<RestaurantStaffDto> Create(Guid restaurantId, Guid userId, CancellationToken ct)
    {
        await access.RequireAdmin(ct);
        ValidateIds(restaurantId, userId);
        var employee = await identity.GetUserAsync(userId, ct);
        if (employee.RestaurantId.HasValue && employee.RestaurantId != restaurantId)
        {
            throw new ManagementException(403, "Tài khoản nhân viên thuộc nhà hàng khác.");
        }
        if (!settings.Value.AssignableRoleIds.Contains(employee.RoleId, StringComparer.Ordinal))
        {
            throw new ManagementException(403, "Chỉ các vai trò nhân viên hợp lệ mới có thể được phân công vào nhà hàng.");
        }

        return await transaction.ExecuteAsync(async () =>
        {
            await RequireRestaurant(restaurantId, ct);
            var assignment = await staff.LockAsync(restaurantId, userId, ct);
            if (assignment?.IsActive == true)
            {
                throw new ManagementException(409, "Phân công nhân viên vào nhà hàng đã đang hoạt động.");
            }

            if (assignment is null)
            {
                assignment = new RestaurantStaff
                {
                    RestaurantId = restaurantId,
                    UserIdRef = userId,
                    IsActive = true
                };
                await staff.AddAsync(assignment);
            }
            else
            {
                assignment.IsActive = true;
            }

            return Map(assignment);
        }, ct);
    }

    public async Task<IReadOnlyList<RestaurantStaffDto>> GetAll(Guid restaurantId, CancellationToken ct)
    {
        var actor = await identity.GetCurrentAsync(ct);
        if (!actor.IsAdministrator && (actor.RoleId != "admin" || actor.RestaurantId != restaurantId
            || !await staff.IsActiveAsync(restaurantId, actor.UserId, ct)))
        {
            throw new ManagementException(403, "Bạn không có quyền xem phân công của nhà hàng này.");
        }

        await RequireRestaurant(restaurantId, ct);
        var result = (await staff.GetAllAsync(restaurantId, ct)).Select(Map).ToArray();
        if (profiles is not null && result.Length > 0)
        {
            await profiles.FillAsync(restaurantId, result, ct);
        }

        return result;
    }

    public async Task<RestaurantStaffDto> Delete(Guid restaurantId, Guid userId, CancellationToken ct)
    {
        await access.RequireAdmin(ct);
        ValidateIds(restaurantId, userId);
        return await transaction.ExecuteAsync(async () =>
        {
            await RequireRestaurant(restaurantId, ct);
            var assignment = await staff.LockAsync(restaurantId, userId, ct)
                ?? throw new ManagementException(404, "Không tìm thấy phân công nhà hàng của nhân viên.");
            if (!assignment.IsActive)
            {
                throw new ManagementException(409, "Phân công nhân viên vào nhà hàng đã ngừng hoạt động.");
            }

            assignment.IsActive = false;
            return Map(assignment);
        }, ct);
    }

    private async Task RequireRestaurant(Guid restaurantId, CancellationToken ct)
    {
        if (restaurantId == Guid.Empty)
        {
            throw new ManagementException(400, "Cần có mã nhà hàng hợp lệ.");
        }

        if (await restaurants.GetByIdAsync(restaurantId, ct) is null)
        {
            throw new ManagementException(404, "Không tìm thấy nhà hàng.");
        }
    }

    private static void ValidateIds(Guid restaurantId, Guid userId)
    {
        if (restaurantId == Guid.Empty || userId == Guid.Empty)
        {
            throw new ManagementException(400, "Cần có mã nhà hàng và mã nhân viên hợp lệ.");
        }
    }

    private static RestaurantStaffDto Map(RestaurantStaff value)
    {
        return new RestaurantStaffDto
        {
            RestaurantId = value.RestaurantId,
            UserIdRef = value.UserIdRef,
            IsActive = value.IsActive
        };
    }
}
