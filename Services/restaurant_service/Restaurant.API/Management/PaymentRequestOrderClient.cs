using Backend.Contracts.Ordering;
using Grpc.Core;
using Restaurant.BLL.Interfaces;
using Restaurant.Common.Exceptions;
using Restaurant.Common.Settings;
using Microsoft.Extensions.Options;

namespace Restaurant.API.Management;

public sealed class PaymentRequestOrderClient(CheckoutLockGrpc.CheckoutLockGrpcClient client,
    IHttpContextAccessor http, IOptions<ServiceRequestSettings> settings) : IPaymentRequestOrderClient
{
    public async Task RequireFrozenAsync(Guid sessionId, Guid checkoutId, CancellationToken ct)
    {
        var headers = new Metadata();
        var authorization = http.HttpContext?.Request.Headers.Authorization.ToString();
        if (!string.IsNullOrWhiteSpace(authorization))
        {
            headers.Add("authorization", authorization);
        }

        try
        {
            var reply = await client.BeginAsync(new CheckoutLockRequest
            {
                TableSessionId = sessionId.ToString(),
                CheckoutId = checkoutId.ToString()
            }, headers, deadline: DateTime.UtcNow.AddSeconds(settings.Value.DependencyTimeoutSeconds), cancellationToken: ct);
            if (reply.TableSessionId != sessionId.ToString() || reply.CheckoutId != checkoutId.ToString())
            {
                throw new ManagementException(503, "Dữ liệu đơn hàng của phiên bàn không hợp lệ.");
            }

            if (reply.Status != "Frozen" || !reply.HasSourceHash || reply.SourceHash.Length != 64
                || !reply.SourceHash.All(Uri.IsHexDigit))
            {
                throw new ManagementException(409, "Vui lòng xử lý hết đơn chờ xác nhận trước khi hoàn tất kiểm tra thanh toán.");
            }
        }
        catch (RpcException error) when (!ct.IsCancellationRequested)
        {
            throw new ManagementException(error.StatusCode switch
            {
                StatusCode.Unauthenticated => 401,
                StatusCode.PermissionDenied => 403,
                StatusCode.FailedPrecondition or StatusCode.Aborted => 409,
                _ => 503
            }, "Chưa xác minh được đơn hàng của phiên bàn. Vui lòng kiểm tra lại trước khi chốt hóa đơn.");
        }
    }
}
