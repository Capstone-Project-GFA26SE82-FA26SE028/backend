using Backend.Contracts.Restaurant;
using Grpc.Core;
using Identity.BLL.Interfaces;
using Shared.Common.Notifications;

namespace Identity.API.Clients;

public sealed class NotificationRestaurantAccess(RestaurantAccessGrpc.RestaurantAccessGrpcClient client,
    IHttpContextAccessor http) : INotificationRestaurantAccess
{
    public async Task<bool> HasAssignmentAsync(Guid userId, Guid restaurantId, CancellationToken ct)
    {
        var authorization = http.HttpContext?.Request.Headers.Authorization.ToString();
        if (string.IsNullOrWhiteSpace(authorization))
        {
            return false;
        }

        try
        {
            var reply = await client.GetCurrentScopeAsync(new CurrentRestaurantScopeRequest
            {
                RestaurantId = restaurantId.ToString()
            }, new Metadata { { "authorization", authorization } }, DateTime.UtcNow.AddSeconds(5), ct);
            return reply.UserId == userId.ToString() && !reply.IsAdministrator && reply.RestaurantIds.Contains(restaurantId.ToString());
        }
        catch (RpcException exception) when (exception.StatusCode is StatusCode.PermissionDenied or StatusCode.NotFound)
        {
            return false;
        }
        catch (RpcException)
        {
            throw new NotificationException(503, "Chưa xác minh được phân công nhà hàng. Vui lòng thử lại.");
        }
    }
}
