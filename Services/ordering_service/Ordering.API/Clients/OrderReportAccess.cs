using Backend.Contracts.Restaurant;
using Grpc.Core;
using Microsoft.Extensions.Options;
using Ordering.BLL.Interfaces;
using Ordering.Common.Exceptions;
using Ordering.Common.Settings;

namespace Ordering.API.Clients;

public sealed class OrderReportAccess(RestaurantAccessGrpc.RestaurantAccessGrpcClient restaurants,
    IHttpContextAccessor http, IOptions<OrderingSettings> settings) : IOrderReportAccess
{
    public async Task RequireManager(Guid restaurantId, CancellationToken ct)
    {
        var authorization = http.HttpContext?.Request.Headers.Authorization.ToString();
        if (string.IsNullOrWhiteSpace(authorization))
        {
            throw new OrderingException(401, "Yêu cầu phiên đăng nhập nhân viên hợp lệ.");
        }

        var context = await restaurants.GetReportContextAsync(new CurrentRestaurantScopeRequest
        {
            RestaurantId = restaurantId.ToString()
        }, new Metadata
        {
            { "authorization", authorization }
        }, deadline: DateTime.UtcNow.AddSeconds(settings.Value.DependencyTimeoutSeconds), cancellationToken: ct);
        if (!Guid.TryParse(context.RestaurantId, out var owner) || owner != restaurantId)
        {
            throw new OrderingException(403, "Không thể xác minh quyền báo cáo tại nhà hàng.");
        }
    }
}
