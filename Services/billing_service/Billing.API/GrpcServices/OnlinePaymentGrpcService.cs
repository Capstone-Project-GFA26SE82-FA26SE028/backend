using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Backend.Contracts.Billing;
using Billing.BLL.Interfaces;
using Billing.Common.DTOs;
using Billing.Common.Exceptions;
using Grpc.Core;

namespace Billing.API.GrpcServices;

public sealed class OnlinePaymentGrpcService(IOnlinePaymentService service, IConfiguration configuration,
    Shared.Persistence.RestaurantIsolation.RestaurantDataScope scope)
    : OnlinePaymentGrpc.OnlinePaymentGrpcBase
{
    public override async Task<OnlinePaymentReply> Execute(OnlinePaymentRequest request, ServerCallContext context)
    {
        var expected = configuration["Takeaway:ServiceKey"];
        var supplied = context.RequestHeaders.GetValue("x-takeaway-key") ?? "";
        if (string.IsNullOrWhiteSpace(expected) || expected.Length < 32
            || !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(supplied)))
        {
            throw new RpcException(new Status(StatusCode.Unauthenticated, "Yêu cầu xác thực dịch vụ đặt món."));
        }

        try
        {
            if (!Guid.TryParse(request.RestaurantId, out var restaurantId) || restaurantId == Guid.Empty)
            {
                throw new BillingException(400, "Mã nhà hàng không hợp lệ.");
            }

            scope.Select(restaurantId);
            if (request.Action is not ("Create" or "Refresh" or "Cancel" or "Refund" or "VerifyRefund"))
            {
                throw new BillingException(400, "Thao tác thanh toán không hợp lệ.");
            }

            var result = await service.Execute(new OnlinePaymentCommand(Guid.Parse(request.OrderId),
                Guid.Parse(request.RestaurantId), Guid.Parse(request.CustomerUserId),
                decimal.Parse(request.Amount, CultureInfo.InvariantCulture), request.Action,
                request.Reason, request.Reference, request.Evidence), context.CancellationToken);
            return new OnlinePaymentReply
            {
                Status = result.Status,
                CheckoutUrl = result.CheckoutUrl ?? "",
                Amount = result.Amount.ToString(CultureInfo.InvariantCulture),
                PaidAt = result.PaidAt?.ToString("O") ?? "",
                RefundedAt = result.RefundedAt?.ToString("O") ?? "",
                RefundReference = result.RefundReference ?? ""
            };
        }
        catch (PayOsClientException)
        {
            throw new RpcException(new Status(StatusCode.Unavailable, "Chưa xác định được kết quả thanh toán. Vui lòng thử lại với cùng đơn hàng."));
        }
    }
}
