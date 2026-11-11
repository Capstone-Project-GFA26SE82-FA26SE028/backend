using Backend.Contracts.Restaurant;
using Billing.API.Settings;
using Billing.BLL.Interfaces;
using Billing.Common.Exceptions;
using Grpc.Core;
using Microsoft.Extensions.Options;

namespace Billing.API.Clients;

public sealed class PaymentRequestClient(ServiceRequestGrpc.ServiceRequestGrpcClient client,
    IHttpContextAccessor http, IOptions<RestaurantAccessSettings> settings) : IPaymentRequestClient
{
    public async Task CreateAsync(Guid sessionId, Guid checkoutId, string guestToken, CancellationToken ct)
    {
        await Call(options => client.CreateAsync(Request(sessionId, checkoutId, guestToken), options).ResponseAsync,
            sessionId, checkoutId, ct);
    }

    public async Task RequireReviewedAsync(Guid sessionId, Guid checkoutId, CancellationToken ct)
    {
        await GetReviewedWaiterAsync(sessionId, checkoutId, ct);
    }

    public async Task<Guid> GetReviewedWaiterAsync(Guid sessionId, Guid checkoutId, CancellationToken ct)
    {
        var reply = await Call(options => client.GetByIdAsync(Request(sessionId, checkoutId, string.Empty), options).ResponseAsync,
            sessionId, checkoutId, ct);
        if (reply.Status != "Reviewed" || !reply.HasHandledByRef
            || !Guid.TryParse(reply.HandledByRef, out var waiterId) || waiterId == Guid.Empty
            || !reply.HasAcknowledgedAt)
        {
            throw new BillingException(409, "Nhân viên phục vụ phải tiếp nhận và xác nhận đã kiểm tra trước khi chốt hóa đơn.");
        }

        return waiterId;
    }

    public async Task CancelAsync(Guid sessionId, Guid checkoutId, CancellationToken ct)
    {
        ServiceRequestInfo reply;
        try
        {
            reply = await Call(options => client.CancelAsync(Request(sessionId, checkoutId, string.Empty), options).ResponseAsync,
                sessionId, checkoutId, ct);
        }
        catch (BillingException error) when (error.StatusCode == 404)
        {
            return;
        }

        if (reply.Status != "Cancelled")
        {
            throw new BillingException(503, "Chưa xác minh được kết quả hủy yêu cầu thanh toán.");
        }
    }

    private async Task<ServiceRequestInfo> Call(Func<CallOptions, Task<ServiceRequestInfo>> call,
        Guid sessionId, Guid id, CancellationToken ct)
    {
        var headers = new Metadata();
        var authorization = http.HttpContext?.Request.Headers.Authorization.ToString();
        if (!string.IsNullOrWhiteSpace(authorization))
        {
            headers.Add("authorization", authorization);
        }

        try
        {
            var reply = await call(new CallOptions(headers,
                DateTime.UtcNow.AddSeconds(settings.Value.TimeoutSeconds), ct));
            if (reply.TableSessionId != sessionId.ToString() || reply.ServiceRequestId != id.ToString() || reply.Type != "Payment")
            {
                throw new BillingException(503, "Dịch vụ nhà hàng trả về yêu cầu thanh toán không hợp lệ.");
            }

            return reply;
        }
        catch (RpcException error) when (!ct.IsCancellationRequested)
        {
            throw new BillingException(error.StatusCode switch
            {
                StatusCode.InvalidArgument => 400,
                StatusCode.Unauthenticated => 401,
                StatusCode.PermissionDenied => 403,
                StatusCode.NotFound => 404,
                StatusCode.FailedPrecondition or StatusCode.Aborted => 409,
                _ => 503
            }, error.StatusCode is StatusCode.InvalidArgument or StatusCode.Unauthenticated or StatusCode.PermissionDenied
                or StatusCode.FailedPrecondition or StatusCode.Aborted ? error.Status.Detail
                : "Chưa xác minh được yêu cầu thanh toán tại nhà hàng. Vui lòng thử lại với cùng mã yêu cầu.");
        }
    }

    private static PaymentServiceRequest Request(Guid sessionId, Guid id, string guestToken)
    {
        return new PaymentServiceRequest
        {
            TableSessionId = sessionId.ToString(),
            ServiceRequestId = id.ToString(),
            GuestToken = guestToken
        };
    }
}
