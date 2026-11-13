using System.Globalization;
using Backend.Contracts.Billing;
using Billing.BLL.Interfaces;
using Billing.Common.Exceptions;
using Grpc.Core;
using Microsoft.Extensions.Options;
using Shared.Common.Billing;

namespace Billing.API.GrpcServices;

public sealed class PaymentSettlementGrpcService(IPaymentSettlementService service,
    IOptions<PaymentSettlementSettings> settings, TimeProvider clock) : PaymentSettlementGrpc.PaymentSettlementGrpcBase
{
    public override async Task<PaidSettlementReply> GetPaid(SettlementScopeRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.TableSessionId, out var sessionId) || sessionId == Guid.Empty
            || !Guid.TryParse(request.RestaurantId, out var restaurantId) || restaurantId == Guid.Empty)
        {
            throw new BillingException(400, "Yêu cầu mã phiên và nhà hàng hợp lệ.");
        }

        if (!long.TryParse(context.RequestHeaders.GetValue(PaymentSettlementSignature.TimestampHeader),
            NumberStyles.None, CultureInfo.InvariantCulture, out var timestamp)
            || !PaymentSettlementSignature.Verify(PaymentSettlementSignature.ReadPurpose,
                PaymentSettlementSignature.Scope(sessionId, restaurantId), timestamp,
                context.RequestHeaders.GetValue(PaymentSettlementSignature.SignatureHeader) ?? string.Empty,
                settings.Value.SharedKey, clock.GetUtcNow()))
        {
            throw new BillingException(401, "Yêu cầu xác thực nội bộ hợp lệ.");
        }

        var payment = await service.GetPaid(sessionId, restaurantId, context.CancellationToken);
        var signedAt = clock.GetUtcNow().ToUnixTimeSeconds();
        return new PaidSettlementReply
        {
            TableSessionId = payment.TableSessionId.ToString(),
            RestaurantId = payment.RestaurantId.ToString(),
            BillId = payment.BillId.ToString(),
            CheckoutId = payment.CheckoutId.ToString(),
            PaidAt = payment.PaidAt.ToString("O", CultureInfo.InvariantCulture),
            SignatureTime = signedAt,
            Signature = PaymentSettlementSignature.Sign(PaymentSettlementSignature.ProofPurpose,
                PaymentSettlementSignature.Proof(payment), signedAt, settings.Value.SharedKey)
        };
    }
}
