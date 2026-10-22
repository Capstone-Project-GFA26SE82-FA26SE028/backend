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
    IOptions<ReservationSettings> settings) : IRestaurantStaffService
{
    public async Task<RestaurantStaffDto> Create(Guid restaurantId, Guid userId, CancellationToken ct)
    {
        await access.RequireAdmin(ct);
        ValidateIds(restaurantId, userId);
        var employee = await identity.GetUserAsync(userId, ct);
        if (!settings.Value.StaffRoleIds.Contains(employee.RoleId, StringComparer.Ordinal))
        {
            throw new ManagementException(403, "Only configured Waiter/Cashier accounts can receive a reservation assignment.");
        }

        return await transaction.ExecuteAsync(async () =>
        {
            await RequireRestaurant(restaurantId, ct);
            var assignment = await staff.LockAsync(restaurantId, userId, ct);
            if (assignment?.IsActive == true)
            {
                throw new ManagementException(409, "The restaurant assignment is already active.");
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
        await access.RequireAdmin(ct);
        await RequireRestaurant(restaurantId, ct);
        return (await staff.GetAllAsync(restaurantId, ct)).Select(Map).ToArray();
    }

    public async Task<RestaurantStaffDto> Delete(Guid restaurantId, Guid userId, CancellationToken ct)
    {
        await access.RequireAdmin(ct);
        ValidateIds(restaurantId, userId);
        return await transaction.ExecuteAsync(async () =>
        {
            await RequireRestaurant(restaurantId, ct);
            var assignment = await staff.LockAsync(restaurantId, userId, ct)
                ?? throw new ManagementException(404, "Restaurant assignment not found.");
            if (!assignment.IsActive)
            {
                throw new ManagementException(409, "The restaurant assignment is already inactive.");
            }

            assignment.IsActive = false;
            return Map(assignment);
        }, ct);
    }

    private async Task RequireRestaurant(Guid restaurantId, CancellationToken ct)
    {
        if (restaurantId == Guid.Empty)
        {
            throw new ManagementException(400, "A valid restaurant ID is required.");
        }

        if (await restaurants.GetByIdAsync(restaurantId, ct) is null)
        {
            throw new ManagementException(404, "Restaurant not found.");
        }
    }

    private static void ValidateIds(Guid restaurantId, Guid userId)
    {
        if (restaurantId == Guid.Empty || userId == Guid.Empty)
        {
            throw new ManagementException(400, "Valid restaurant and employee IDs are required.");
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
