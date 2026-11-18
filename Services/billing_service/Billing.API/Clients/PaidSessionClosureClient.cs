using Backend.Contracts.Restaurant;
using Billing.API.Settings;
using Billing.BLL.Interfaces;
using Grpc.Core;
using Microsoft.Extensions.Options;
using Shared.Common.Billing;

namespace Billing.API.Clients;

public sealed class PaidSessionClosureClient(PaidSessionClosureGrpc.PaidSessionClosureGrpcClient client,
    IConfiguration configuration, IOptions<RestaurantAccessSettings> settings, ILogger<PaidSessionClosureClient> logger)
    : IPaidSessionClosureClient
{
    public async Task TryCloseAsync(Guid restaurantId, Guid sessionId, CancellationToken ct)
    {
        var key = configuration[PaymentSettlementServiceAccess.ConfigurationKey];
        if (!PaymentSettlementServiceAccess.IsConfigured(key) || ct.IsCancellationRequested)
        {
            return;
        }

        try
        {
            var result = await client.CloseAfterPaymentAsync(new PaidSessionClosureRequest
            {
                RestaurantId = restaurantId.ToString(),
                TableSessionId = sessionId.ToString()
            }, new Metadata { { PaymentSettlementServiceAccess.Header, PaymentSettlementServiceAccess.CreateHeaderValue(key!) } },
                deadline: DateTime.UtcNow.AddSeconds(settings.Value.TimeoutSeconds), cancellationToken: ct);
            if (!result.Closed)
            {
                logger.LogWarning("Đã lưu thanh toán. Phiên {SessionId} chưa được xác nhận đã đóng.", sessionId);
            }
        }
        catch (RpcException error)
        {
            logger.LogWarning("Đã lưu thanh toán. Đóng phiên {SessionId} sẽ được thử lại. Trạng thái RPC {Status}.",
                sessionId, error.StatusCode);
        }
    }
}
