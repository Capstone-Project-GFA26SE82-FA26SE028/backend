using Backend.Contracts.Restaurant;
using Grpc.Core;
using Restaurant.BLL.Interfaces;
using Restaurant.Common.Exceptions;
using Shared.Common.Billing;

namespace Restaurant.API.GrpcServices;

public sealed class PaidSessionClosureGrpcService(ITableSessionService service, IConfiguration configuration)
    : PaidSessionClosureGrpc.PaidSessionClosureGrpcBase
{
    public override async Task<PaidSessionClosureReply> CloseAfterPayment(PaidSessionClosureRequest request, ServerCallContext context)
    {
        if (!PaymentSettlementServiceAccess.Matches(configuration[PaymentSettlementServiceAccess.ConfigurationKey],
            context.RequestHeaders.GetValue(PaymentSettlementServiceAccess.Header)))
        {
            throw new RpcException(new Status(StatusCode.Unauthenticated, "Yêu cầu xác thực dịch vụ thanh toán nội bộ."));
        }

        if (!Guid.TryParse(request.RestaurantId, out var restaurantId) || restaurantId == Guid.Empty
            || !Guid.TryParse(request.TableSessionId, out var sessionId) || sessionId == Guid.Empty)
        {
            throw new ManagementException(400, "Mã nhà hàng hoặc phiên bàn không hợp lệ.");
        }

        return new PaidSessionClosureReply
        {
            Closed = await service.CloseAfterPayment(restaurantId, sessionId, context.CancellationToken)
        };
    }
}
