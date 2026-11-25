using Backend.Contracts.Restaurant;
using Billing.API.Settings;
using Billing.BLL.Interfaces;
using Billing.Common.DTOs;
using Billing.Common.Exceptions;
using Grpc.Core;
using Microsoft.Extensions.Options;

namespace Billing.API.Clients;

public sealed class RestaurantReportContextClient(RestaurantAccessGrpc.RestaurantAccessGrpcClient client,
    IHttpContextAccessor http, IOptions<RestaurantAccessSettings> settings) : IRestaurantReportContextClient
{
    public async Task<RestaurantReportContextDTO> GetAsync(Guid restaurantId, CancellationToken ct)
    {
        var authorization = http.HttpContext?.Request.Headers.Authorization.ToString();
        if (string.IsNullOrWhiteSpace(authorization))
        {
            throw new BillingException(401, "Yêu cầu phiên đăng nhập nhân viên hợp lệ.");
        }

        var headers = new Metadata
        {
            { "authorization", authorization }
        };
        var reply = await client.GetReportContextAsync(new CurrentRestaurantScopeRequest
        {
            RestaurantId = restaurantId.ToString()
        }, headers, deadline: DateTime.UtcNow.AddSeconds(settings.Value.TimeoutSeconds), cancellationToken: ct);
        if (!Guid.TryParse(reply.RestaurantId, out var owner) || owner != restaurantId)
        {
            throw new BillingException(503, "Không thể xác minh ngữ cảnh báo cáo của nhà hàng.");
        }

        return new RestaurantReportContextDTO(owner, reply.TimeZone);
    }
}
