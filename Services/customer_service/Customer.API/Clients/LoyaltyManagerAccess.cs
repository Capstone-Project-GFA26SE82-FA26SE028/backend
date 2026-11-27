using Backend.Contracts.Restaurant;
using Customer.BLL.Interfaces;
using Customer.Common.Exceptions;
using Grpc.Core;

namespace Customer.API.Clients;

public sealed class LoyaltyManagerAccess(
    RestaurantAccessGrpc.RestaurantAccessGrpcClient restaurants,
    IHttpContextAccessor http) : ILoyaltyManagerAccess
{
    public async Task EnsureManagerAccessAsync(Guid restaurantId, CancellationToken ct)
    {
        if (restaurantId == Guid.Empty)
        {
            throw new CustomerException(400, "Vui lòng cung cấp mã nhà hàng.");
        }

        var authorization = http.HttpContext?.Request.Headers.Authorization.ToString();
        var headers = new Metadata();
        if (!string.IsNullOrWhiteSpace(authorization))
        {
            headers.Add("authorization", authorization);
        }

        try
        {
            var scope = await restaurants.GetCurrentScopeAsync(new CurrentRestaurantScopeRequest
            {
                RestaurantId = restaurantId.ToString()
            }, headers, deadline: DateTime.UtcNow.AddSeconds(10), cancellationToken: ct);

            if (!scope.IsAdministrator && (scope.RoleId != "manager" || !scope.RestaurantIds.Contains(restaurantId.ToString())))
            {
                throw new CustomerException(403, "Chỉ quản lý được phân công tại nhà hàng này mới có quyền cấu hình hạng và chính sách tích điểm.");
            }
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.PermissionDenied)
        {
            throw new CustomerException(403, ex.Status.Detail);
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.Unauthenticated)
        {
            throw new CustomerException(401, "Vui lòng đăng nhập tài khoản quản lý.");
        }
    }
}
