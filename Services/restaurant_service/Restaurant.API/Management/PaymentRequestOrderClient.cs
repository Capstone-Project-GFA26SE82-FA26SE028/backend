using Backend.Contracts.Ordering;
using Grpc.Core;
using Restaurant.BLL.Interfaces;
using Restaurant.Common.DTOs;
using Restaurant.Common.Exceptions;

namespace Restaurant.API.Management;

public sealed class PaymentRequestOrderClient(BillingOrderGrpc.BillingOrderGrpcClient client,
    IHttpContextAccessor accessor) : IPaymentRequestOrderClient
{
    public async Task<PaymentRequestReviewDto> GetReviewAsync(Guid sessionId, Guid restaurantId, CancellationToken ct)
    {
        var authorization = accessor.HttpContext?.Request.Headers.Authorization.ToString();
        if (string.IsNullOrWhiteSpace(authorization))
        {
            throw new ManagementException(401, "Yêu cầu phiên đăng nhập nhân viên hợp lệ.");
        }

        BillingOrderSnapshot reply;
        try
        {
            reply = await client.GetForPaymentReviewAsync(new BillingOrderRequest
            {
                TableSessionId = sessionId.ToString()
            }, new Metadata { { "authorization", authorization } },
                deadline: DateTime.UtcNow.AddSeconds(5), cancellationToken: ct);
        }
        catch (RpcException exception) when (!ct.IsCancellationRequested)
        {
            var status = exception.StatusCode switch
            {
                StatusCode.Unauthenticated => 401,
                StatusCode.PermissionDenied => 403,
                StatusCode.NotFound => 404,
                StatusCode.FailedPrecondition or StatusCode.Aborted => 409,
                _ => 503
            };
            throw new ManagementException(status, "Không thể xác minh dữ liệu đơn hàng của phiên bàn.");
        }

        if (!Guid.TryParse(reply.TableSessionId, out var returnedSession) || returnedSession != sessionId
            || !Guid.TryParse(reply.RestaurantId, out var returnedRestaurant) || returnedRestaurant != restaurantId
            || reply.PendingOrderCount < 0)
        {
            throw InvalidSnapshot();
        }

        var items = new List<PaymentRequestReviewItemDto>(reply.Items.Count);
        foreach (var line in reply.Items)
        {
            if (!Guid.TryParse(line.OrderItemId, out var itemId) || itemId == Guid.Empty
                || !Guid.TryParse(line.OriginalTableId, out var tableId) || tableId == Guid.Empty
                || line.Quantity <= 0 || string.IsNullOrWhiteSpace(line.Name) || string.IsNullOrWhiteSpace(line.Status))
            {
                throw InvalidSnapshot();
            }

            items.Add(new PaymentRequestReviewItemDto(itemId, tableId, line.Name, line.Quantity, line.Status));
        }

        return new PaymentRequestReviewDto(restaurantId, reply.PendingOrderCount, items);
    }

    private static ManagementException InvalidSnapshot()
    {
        return new ManagementException(503, "Dịch vụ gọi món trả về dữ liệu kiểm tra thanh toán không hợp lệ.");
    }
}
