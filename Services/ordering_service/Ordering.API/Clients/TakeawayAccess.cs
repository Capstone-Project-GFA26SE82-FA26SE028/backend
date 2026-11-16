using Backend.Contracts.Identity;
using Backend.Contracts.Restaurant;
using Grpc.Core;
using Microsoft.Extensions.Options;
using Ordering.BLL.Interfaces;
using Ordering.Common.Exceptions;
using Ordering.Common.Settings;

namespace Ordering.API.Clients;

public sealed class TakeawayAccess(UserGrpc.UserGrpcClient identity,
    RestaurantAccessGrpc.RestaurantAccessGrpcClient restaurants, IHttpContextAccessor http,
    IOptions<OrderingSettings> settings) : ITakeawayAccess
{
    public async Task<TakeawayActor> GetActorAsync(Guid restaurantId, CancellationToken ct)
    {
        if (restaurantId == Guid.Empty)
        {
            throw new OrderingException(400, "Vui lòng cung cấp nhà hàng.");
        }

        var authorization = http.HttpContext?.Request.Headers.Authorization.ToString();
        if (string.IsNullOrWhiteSpace(authorization))
        {
            throw new OrderingException(401, "Vui lòng đăng nhập tài khoản. Phiên truy cập qua mã QR bàn không được chấp nhận cho thao tác này.");
        }

        var headers = new Metadata { { "authorization", authorization } };
        var deadline = DateTime.UtcNow.AddSeconds(settings.Value.DependencyTimeoutSeconds);
        var user = await identity.GetMeAsync(new GetMeRequest(), headers, deadline: deadline, cancellationToken: ct);
        if (!user.IsSuccess || user.Result is null || !user.Result.IsActive
            || !Guid.TryParse(user.Result.UserId, out var userId) || userId == Guid.Empty)
        {
            throw new OrderingException(user.StatusCode >= 500 ? 503 : 401, "Tài khoản không hợp lệ, đã bị vô hiệu hóa hoặc phiên đăng nhập đã bị thu hồi.");
        }

        if (user.Result.RoleId == "customer")
        {
            return new TakeawayActor(userId, true, "customer");
        }

        if (user.Result.RoleId is not ("waiter" or "cashier"))
        {
            throw new OrderingException(403, "Yêu cầu tài khoản khách hàng hoặc nhân viên phục vụ, thu ngân được phân công.");
        }

        var scope = await restaurants.GetCurrentScopeAsync(new CurrentRestaurantScopeRequest
        {
            RestaurantId = restaurantId.ToString()
        }, headers, deadline: deadline, cancellationToken: ct);
        if (scope.UserId != userId.ToString() || scope.IsAdministrator
            || !scope.RestaurantIds.Contains(restaurantId.ToString()))
        {
            throw new OrderingException(403, "Bạn phải đang được phân công làm việc tại nhà hàng này.");
        }

        return new TakeawayActor(userId, false, user.Result.RoleId);
    }
}
