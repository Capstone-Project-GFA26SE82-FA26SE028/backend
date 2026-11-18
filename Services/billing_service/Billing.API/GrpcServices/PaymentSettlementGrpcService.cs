using Backend.Contracts.Billing;
using Billing.BLL.Interfaces;
using Billing.Common.Exceptions;
using Grpc.Core;
using Shared.Common.Billing;
using Shared.Persistence.RestaurantIsolation;

namespace Billing.API.GrpcServices;

public sealed class PaymentSettlementGrpcService(IPaymentSettlementService service, IConfiguration configuration,
    RestaurantDataScope scope) : PaymentSettlementGrpc.PaymentSettlementGrpcBase
{
    public override async Task<VerifiedCheckoutReply> GetVerifiedCheckout(VerifiedCheckoutRequest request, ServerCallContext context)
    {
        if (!PaymentSettlementServiceAccess.Matches(configuration[PaymentSettlementServiceAccess.ConfigurationKey],
            context.RequestHeaders.GetValue(PaymentSettlementServiceAccess.Header)))
        {
            throw new RpcException(new Status(StatusCode.Unauthenticated, "Yêu cầu xác thực dịch vụ thanh toán."));
        }

        if (!Guid.TryParse(request.RestaurantId, out var restaurantId) || restaurantId == Guid.Empty
            || !Guid.TryParse(request.TableSessionId, out var sessionId) || sessionId == Guid.Empty)
        {
            throw new BillingException(400, "Mã nhà hàng hoặc phiên bàn không hợp lệ.");
        }

        scope.Select(restaurantId);
        var checkoutId = await service.GetVerifiedCheckout(restaurantId, sessionId, context.CancellationToken);
        var reply = new VerifiedCheckoutReply();
        if (checkoutId.HasValue)
        {
            reply.CheckoutId = checkoutId.Value.ToString();
        }

        return reply;
    }
}
