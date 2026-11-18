using Backend.Contracts.Billing;
using Grpc.Core;
using Restaurant.BLL.Interfaces;
using Restaurant.Common.Exceptions;
using Shared.Common.Billing;

namespace Restaurant.API.Management;

public sealed class VerifiedPaymentClient(PaymentSettlementGrpc.PaymentSettlementGrpcClient client, IConfiguration configuration)
    : IVerifiedPaymentClient
{
    public async Task<Guid?> GetCheckoutAsync(Guid restaurantId, Guid sessionId, CancellationToken ct)
    {
        var key = configuration[PaymentSettlementServiceAccess.ConfigurationKey];
        if (!PaymentSettlementServiceAccess.IsConfigured(key))
        {
            throw new ManagementException(503, "Chưa cấu hình xác thực dịch vụ thanh toán nội bộ.");
        }

        var reply = await client.GetVerifiedCheckoutAsync(new VerifiedCheckoutRequest
        {
            RestaurantId = restaurantId.ToString(),
            TableSessionId = sessionId.ToString()
        }, new Metadata { { PaymentSettlementServiceAccess.Header, PaymentSettlementServiceAccess.CreateHeaderValue(key!) } },
            DateTime.UtcNow.AddSeconds(4), ct);
        if (!reply.HasCheckoutId)
        {
            return null;
        }

        if (!Guid.TryParse(reply.CheckoutId, out var checkout) || checkout == Guid.Empty)
        {
            throw new ManagementException(503, "Dịch vụ thanh toán trả về mã checkout không hợp lệ.");
        }

        return checkout;
    }
}
