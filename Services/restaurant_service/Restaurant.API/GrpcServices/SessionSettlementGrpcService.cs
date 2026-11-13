using System.Globalization;
using Backend.Contracts.Restaurant;
using Grpc.Core;
using Microsoft.Extensions.Options;
using Restaurant.BLL.Interfaces;
using Restaurant.Common.Exceptions;
using Shared.Common.Billing;

namespace Restaurant.API.GrpcServices;

public sealed class SessionSettlementGrpcService(ITableSessionService service,
    IOptions<PaymentSettlementSettings> settings, TimeProvider clock) : SessionSettlementGrpc.SessionSettlementGrpcBase
{
    public override async Task<SessionPaymentCloseReply> ClosePaidSession(SessionPaymentCloseRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.TableSessionId, out var sessionId) || sessionId == Guid.Empty
            || !Guid.TryParse(request.RestaurantId, out var restaurantId) || restaurantId == Guid.Empty
            || !Guid.TryParse(request.BillId, out var billId) || billId == Guid.Empty
            || !Guid.TryParse(request.CheckoutId, out var checkoutId) || checkoutId == Guid.Empty
            || !DateTimeOffset.TryParseExact(request.PaidAt, "O", CultureInfo.InvariantCulture, DateTimeStyles.None, out var paidAt))
        {
            throw new ManagementException(400, "Chứng từ thanh toán không hợp lệ.");
        }

        var payment = new PaidSessionSettlement(sessionId, restaurantId, billId, checkoutId, paidAt);
        if (!PaymentSettlementSignature.Verify(PaymentSettlementSignature.ProofPurpose, PaymentSettlementSignature.Proof(payment),
            request.SignatureTime, request.Signature, settings.Value.SharedKey, clock.GetUtcNow()))
        {
            throw new ManagementException(401, "Yêu cầu chứng từ thanh toán đã xác thực từ Billing.");
        }

        var result = await service.ClosePaidSession(payment, context.CancellationToken);
        var timestamp = clock.GetUtcNow().ToUnixTimeSeconds();
        return new SessionPaymentCloseReply
        {
            TableSessionId = result.TableSessionId.ToString(),
            RestaurantId = result.RestaurantId.ToString(),
            ClosedAt = result.ClosedAt.ToString("O", CultureInfo.InvariantCulture),
            SignatureTime = timestamp,
            Signature = PaymentSettlementSignature.Sign(PaymentSettlementSignature.ClosedPurpose,
                PaymentSettlementSignature.Closed(sessionId, restaurantId, result.ClosedAt), timestamp, settings.Value.SharedKey)
        };
    }
}
