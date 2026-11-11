using Backend.Contracts.Billing;
using Grpc.Core;
using Microsoft.Extensions.Options;
using Restaurant.BLL.Interfaces;
using Restaurant.Common.Exceptions;
using Restaurant.Common.Settings;

namespace Restaurant.API.Management;

public sealed class SessionPaymentClient(PaymentGrpc.PaymentGrpcClient client,
    IHttpContextAccessor http, IOptions<ServiceRequestSettings> settings) : ISessionPaymentClient
{
    public async Task RequirePaidAsync(Guid sessionId, Guid restaurantId, CancellationToken ct)
    {
        var headers = new Metadata();
        var authorization = http.HttpContext?.Request.Headers.Authorization.ToString();
        if (!string.IsNullOrWhiteSpace(authorization))
        {
            headers.Add("authorization", authorization);
        }

        try
        {
            await client.RequirePaidAsync(new DeparturePaymentRequest
            {
                TableSessionId = sessionId.ToString(),
                RestaurantId = restaurantId.ToString()
            }, headers, deadline: DateTime.UtcNow.AddSeconds(settings.Value.DependencyTimeoutSeconds), cancellationToken: ct);
        }
        catch (RpcException error) when (!ct.IsCancellationRequested)
        {
            var status = error.StatusCode switch
            {
                StatusCode.Unauthenticated => 401,
                StatusCode.PermissionDenied => 403,
                StatusCode.InvalidArgument => 400,
                StatusCode.AlreadyExists or StatusCode.FailedPrecondition or StatusCode.Aborted or StatusCode.NotFound => 409,
                _ => 503
            };
            throw new ManagementException(status, "Chưa xác minh được thanh toán hoàn tất. Chưa thể kết thúc phiên bàn.");
        }
    }
}
