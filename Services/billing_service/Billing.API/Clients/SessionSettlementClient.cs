using System.Globalization;
using Backend.Contracts.Restaurant;
using Billing.BLL.Interfaces;
using Microsoft.Extensions.Options;
using Shared.Common.Billing;

namespace Billing.API.Clients;

public sealed class SessionSettlementClient(SessionSettlementGrpc.SessionSettlementGrpcClient client,
    IOptions<PaymentSettlementSettings> settings, TimeProvider clock, ILogger<SessionSettlementClient> logger) : ISessionSettlementClient
{
    public async Task<bool> TryCloseAsync(PaidSessionSettlement payment, CancellationToken ct)
    {
        try
        {
            var now = clock.GetUtcNow();
            var signedAt = now.ToUnixTimeSeconds();
            var reply = await client.ClosePaidSessionAsync(new SessionPaymentCloseRequest
            {
                TableSessionId = payment.TableSessionId.ToString(),
                RestaurantId = payment.RestaurantId.ToString(),
                BillId = payment.BillId.ToString(),
                CheckoutId = payment.CheckoutId.ToString(),
                PaidAt = payment.PaidAt.ToString("O", CultureInfo.InvariantCulture),
                SignatureTime = signedAt,
                Signature = PaymentSettlementSignature.Sign(PaymentSettlementSignature.ProofPurpose,
                    PaymentSettlementSignature.Proof(payment), signedAt, settings.Value.SharedKey)
            }, deadline: now.UtcDateTime.AddSeconds(settings.Value.TimeoutSeconds), cancellationToken: ct);
            if (reply.TableSessionId != payment.TableSessionId.ToString() || reply.RestaurantId != payment.RestaurantId.ToString()
                || !DateTimeOffset.TryParseExact(reply.ClosedAt, "O", CultureInfo.InvariantCulture, DateTimeStyles.None, out var closedAt)
                || !PaymentSettlementSignature.Verify(PaymentSettlementSignature.ClosedPurpose,
                    PaymentSettlementSignature.Closed(payment.TableSessionId, payment.RestaurantId, closedAt),
                    reply.SignatureTime, reply.Signature, settings.Value.SharedKey, clock.GetUtcNow()))
            {
                throw new InvalidOperationException("Unverified session closure response.");
            }

            return true;
        }
        catch (Exception error)
        {
            logger.LogWarning("Đã ghi nhận thanh toán, chưa xác nhận đóng phiên {SessionId}; loại lỗi {ErrorType}. Worker sẽ thử lại.",
                payment.TableSessionId, error.GetType().Name);
            return false;
        }
    }
}
