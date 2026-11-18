using Backend.Contracts.Restaurant;
using Billing.API.Settings;
using Billing.BLL.Interfaces;
using Billing.Common.Exceptions;
using Grpc.Core;
using Microsoft.Extensions.Options;

namespace Billing.API.Clients;

public sealed class PaymentReviewClient(ServiceRequestGrpc.ServiceRequestGrpcClient client,
    IHttpContextAccessor accessor, IOptions<RestaurantAccessSettings> settings) : IPaymentReviewClient
{
    public async Task<(Guid RestaurantId, int PendingOrderCount)> GetReviewAsync(Guid sessionId, Guid requestId, CancellationToken ct)
    {
        var reply = await Call(sessionId, requestId, async (request, options) => await client.GetReviewAsync(request, options), ct);
        if (reply.PendingOrderCount < 0)
        {
            throw new BillingException(503, "Nhà hàng trả về dữ liệu kiểm tra yêu cầu thanh toán không hợp lệ.");
        }

        return (RestaurantId(reply.RestaurantId), reply.PendingOrderCount);
    }

    public async Task<Guid> GetResultRestaurantAsync(Guid sessionId, Guid requestId, CancellationToken ct)
    {
        var reply = await Call(sessionId, requestId, async (request, options) => await client.GetResultAccessAsync(request, options), ct);
        return RestaurantId(reply.RestaurantId);
    }

    private async Task<T> Call<T>(Guid sessionId, Guid requestId, Func<PaymentServiceRequest, CallOptions, Task<T>> action,
        CancellationToken ct)
    {
        var authorization = accessor.HttpContext?.Request.Headers.Authorization.ToString();
        if (string.IsNullOrWhiteSpace(authorization))
        {
            throw new BillingException(401, "Yêu cầu phiên đăng nhập nhân viên hợp lệ.");
        }

        try
        {
            return await action(new PaymentServiceRequest
            {
                TableSessionId = sessionId.ToString(),
                ServiceRequestId = requestId.ToString()
            }, new CallOptions(new Metadata { { "authorization", authorization } },
                DateTime.UtcNow.AddSeconds(settings.Value.TimeoutSeconds), ct));
        }
        catch (RpcException error) when (!ct.IsCancellationRequested)
        {
            var code = error.StatusCode switch
            {
                StatusCode.Unauthenticated => 401,
                StatusCode.PermissionDenied => 403,
                StatusCode.NotFound => 404,
                StatusCode.FailedPrecondition or StatusCode.Aborted => 409,
                _ => 503
            };
            throw new BillingException(code, code == 503 ? "Không thể kiểm tra yêu cầu thanh toán. Vui lòng thử lại." : error.Status.Detail);
        }
    }

    private static Guid RestaurantId(string value)
    {
        if (!Guid.TryParse(value, out var restaurantId) || restaurantId == Guid.Empty)
        {
            throw new BillingException(503, "Nhà hàng trả về dữ liệu kiểm tra yêu cầu thanh toán không hợp lệ.");
        }

        return restaurantId;
    }
}
