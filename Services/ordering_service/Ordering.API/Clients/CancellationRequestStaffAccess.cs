using Backend.Contracts.Restaurant;
using Grpc.Core;
using Ordering.BLL.Interfaces;
using Ordering.Common.Exceptions;
using Shared.Persistence.RestaurantIsolation;

namespace Ordering.API.Clients;

public sealed class CancellationRequestStaffAccess(RestaurantAccessGrpc.RestaurantAccessGrpcClient restaurants,
    RestaurantDataScope scope, IHttpContextAccessor http) : ICancellationRequestStaffAccess
{
    public async Task RequireAsync(CancellationToken ct)
    {
        var authorization = http.HttpContext?.Request.Headers.Authorization.ToString();
        if (string.IsNullOrWhiteSpace(authorization))
        {
            throw new OrderingException(401, "Vui lòng đăng nhập tài khoản nhân viên.");
        }

        var restaurantId = scope.RestaurantId
            ?? throw new OrderingException(403, "Cần chọn nhà hàng.");
        var reply = await restaurants.GetCurrentScopeAsync(new CurrentRestaurantScopeRequest
        {
            RestaurantId = restaurantId.ToString()
        }, new Metadata { { "authorization", authorization } }, DateTime.UtcNow.AddSeconds(5), ct);
        if (reply.IsAdministrator || reply.RoleId != "waiter"
            || !reply.RestaurantIds.Any(id => Guid.TryParse(id, out var parsed) && parsed == restaurantId))
        {
            throw new OrderingException(403, "Chỉ nhân viên phục vụ được phân công tại nhà hàng được xem yêu cầu hủy.");
        }
    }
}
