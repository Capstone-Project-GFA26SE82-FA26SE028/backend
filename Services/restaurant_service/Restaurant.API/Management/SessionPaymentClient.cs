using System.Globalization;
using Backend.Contracts.Billing;
using Grpc.Core;
using Microsoft.Extensions.Options;
using Restaurant.BLL.Interfaces;
using Restaurant.Common.Exceptions;
using Shared.Common.Billing;

namespace Restaurant.API.Management;

public sealed class SessionPaymentClient(PaymentSettlementGrpc.PaymentSettlementGrpcClient client,
    IOptions<PaymentSettlementSettings> settings, TimeProvider clock) : ISessionPaymentClient
{
    public async Task<PaidSessionSettlement?> GetPaidAsync(Guid sessionId, Guid restaurantId, CancellationToken ct)
    {
        try
        {
            var now = clock.GetUtcNow();
            var timestamp = now.ToUnixTimeSeconds();
            var reply = await client.GetPaidAsync(new SettlementScopeRequest
            {
                TableSessionId = sessionId.ToString(),
                RestaurantId = restaurantId.ToString()
            }, new Metadata
            {
                { PaymentSettlementSignature.TimestampHeader, timestamp.ToString(CultureInfo.InvariantCulture) },
                { PaymentSettlementSignature.SignatureHeader, PaymentSettlementSignature.Sign(PaymentSettlementSignature.ReadPurpose,
                    PaymentSettlementSignature.Scope(sessionId, restaurantId), timestamp, settings.Value.SharedKey) }
            }, deadline: now.UtcDateTime.AddSeconds(settings.Value.TimeoutSeconds), cancellationToken: ct);
            if (reply.TableSessionId != sessionId.ToString() || reply.RestaurantId != restaurantId.ToString()
                || !Guid.TryParse(reply.BillId, out var billId) || billId == Guid.Empty
                || !Guid.TryParse(reply.CheckoutId, out var checkoutId) || checkoutId == Guid.Empty
                || !DateTimeOffset.TryParseExact(reply.PaidAt, "O", CultureInfo.InvariantCulture, DateTimeStyles.None, out var paidAt))
            {
                throw new ManagementException(503, "Dữ liệu xác nhận thanh toán không hợp lệ.");
            }

            var payment = new PaidSessionSettlement(sessionId, restaurantId, billId, checkoutId, paidAt);
            if (!PaymentSettlementSignature.Verify(PaymentSettlementSignature.ProofPurpose, PaymentSettlementSignature.Proof(payment),
                reply.SignatureTime, reply.Signature, settings.Value.SharedKey, clock.GetUtcNow()))
            {
                throw new ManagementException(503, "Không xác minh được chữ ký chứng từ thanh toán.");
            }

            return payment;
        }
        catch (RpcException error) when (!ct.IsCancellationRequested && error.StatusCode is StatusCode.AlreadyExists or StatusCode.FailedPrecondition or StatusCode.NotFound)
        {
            return null;
        }
        catch (RpcException) when (!ct.IsCancellationRequested)
        {
            throw new ManagementException(503, "Chưa xác minh được thanh toán hoàn tất. Phiên vẫn được giữ để thử lại.");
        }
    }
}
