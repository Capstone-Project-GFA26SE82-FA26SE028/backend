using Backend.Contracts.Restaurant;
using Catalog.BLL.Interfaces;
using Grpc.Core;

namespace Catalog.API.Authorization;

public sealed class RestaurantMenuAccess(RestaurantAccessGrpc.RestaurantAccessGrpcClient client,
    IHttpContextAccessor http) : IRestaurantMenuAccess
{
    public async Task RequireManagerAsync(Guid restaurantId, CancellationToken ct)
    {
        var reply = await client.GetCurrentScopeAsync(new CurrentRestaurantScopeRequest
        {
            RestaurantId = restaurantId.ToString()
        }, new Metadata { { "authorization", http.HttpContext?.Request.Headers.Authorization.ToString() ?? "" } },
            deadline: DateTime.UtcNow.AddSeconds(5), cancellationToken: ct);
        if (reply.IsAdministrator || reply.RoleId != "manager" || !reply.RestaurantIds.Contains(restaurantId.ToString()))
        {
            throw new RpcException(new Status(StatusCode.PermissionDenied, "Yêu cầu quản lý được phân công tại nhà hàng."));
        }
    }
}
