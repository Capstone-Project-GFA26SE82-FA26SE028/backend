using Backend.Contracts.Identity;
using Backend.Contracts.Restaurant;
using Grpc.Core;
using Ordering.BLL.Interfaces;
using Ordering.Common.DTOs;
using Ordering.Common.Exceptions;
using Shared.Persistence.RestaurantIsolation;

namespace Ordering.API.Clients;

public sealed class KitchenAssignmentAccess(UserGrpc.UserGrpcClient identity,
    RestaurantAccessGrpc.RestaurantAccessGrpcClient restaurants,
    RestaurantStaffGrpc.RestaurantStaffGrpcClient staff, RestaurantDataScope scope,
    IHttpContextAccessor http, IConfiguration configuration) : IKitchenAssignmentAccess
{
    private Guid RestaurantId => scope.RestaurantId
        ?? throw new OrderingException(403, "Cần chọn nhà hàng để quản lý phân công bếp.");

    public async Task RequireManager(CancellationToken ct)
    {
        var authorization = http.HttpContext?.Request.Headers.Authorization.ToString();
        if (string.IsNullOrWhiteSpace(authorization))
        {
            throw new OrderingException(401, "Vui lòng đăng nhập.");
        }

        var result = await restaurants.GetCurrentScopeAsync(new CurrentRestaurantScopeRequest
        {
            RestaurantId = RestaurantId.ToString()
        }, new Metadata { { "authorization", authorization } }, DateTime.UtcNow.AddSeconds(5), ct);
        if (result.IsAdministrator || result.RoleId is not ("admin" or "manager")
            || !result.RestaurantIds.Any(id => Guid.TryParse(id, out var parsed) && parsed == RestaurantId))
        {
            throw new OrderingException(403, "Chỉ admin nhà hàng hoặc quản lý được phân công bếp trong nhà hàng của mình.");
        }
    }

    public async Task<IReadOnlyList<KitchenEmployeeDTO>> GetEmployees(CancellationToken ct)
    {
        var assignments = await staff.GetNotificationRecipientsAsync(new RestaurantStaffListRequest
        {
            RestaurantId = RestaurantId.ToString()
        }, Headers("Notifications:ServiceKey", "x-notification-key"), DateTime.UtcNow.AddSeconds(5), ct);
        var employees = new List<KitchenEmployeeDTO>();
        foreach (var batch in assignments.Items.Where(item => item.IsActive)
            .Select(item => item.UserIdRef).Distinct().Chunk(100))
        {
            var request = new StaffProfilesRequest
            {
                RestaurantId = RestaurantId.ToString()
            };
            request.UserIds.AddRange(batch);
            var reply = await identity.GetStaffProfilesAsync(request,
                Headers("StaffProvisioning:ServiceKey", "x-staff-provisioning-key"), DateTime.UtcNow.AddSeconds(5), ct);
            if (!reply.IsSuccess)
            {
                throw new OrderingException(503, "Chưa lấy được thông tin nhân viên.");
            }

            foreach (var user in reply.Items)
            {
                if (!Guid.TryParse(user.RestaurantId, out var restaurantId) || restaurantId != RestaurantId
                    || !batch.Contains(user.UserId) || !Guid.TryParse(user.UserId, out var userId))
                {
                    continue;
                }

                employees.Add(new KitchenEmployeeDTO
                {
                    UserId = userId,
                    FullName = user.HasFullName ? user.FullName : null,
                    UserName = user.UserName,
                    Email = user.Email,
                    AvatarUrl = user.HasAvatarUrl ? user.AvatarUrl : null,
                    RoleId = user.RoleId,
                    IsActive = user.IsActive
                });
            }
        }

        return employees;
    }

    public async Task RequireEmployee(Guid userId, CancellationToken ct)
    {
        if (!(await GetEmployees(ct)).Any(item => item.UserId == userId && item.IsActive && item.RoleId == "kitchen"))
        {
            throw new OrderingException(409, "Nhân viên phải có vai trò bếp, đang hoạt động và được phân công tại nhà hàng này.");
        }
    }

    private Metadata Headers(string setting, string header)
    {
        var key = configuration[setting];
        if (string.IsNullOrEmpty(key) || key.Length < 32)
        {
            throw new OrderingException(503, "Chưa cấu hình khóa tra cứu nhân viên.");
        }

        return new Metadata { { header, key } };
    }
}
