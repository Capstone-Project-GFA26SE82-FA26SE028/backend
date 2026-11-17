using Backend.Contracts.Billing;
using Billing.BLL.Interfaces;
using Grpc.Core;
using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Billing.DAL.Models;
using Shared.Persistence.RestaurantIsolation;

namespace Billing.API.GrpcServices;

public sealed class PaymentWebhookGrpcService(IPaymentWebhookService service, IOnlinePaymentService online,
    BillingDbContext db, RestaurantDataScope scope, IPayOsClient provider) : PaymentWebhookGrpc.PaymentWebhookGrpcBase
{
    public override async Task<PaymentWebhookReply> Process(PaymentWebhookRequest request, ServerCallContext context)
    {
        if (System.Text.Encoding.UTF8.GetByteCount(request.Payload) > 65536)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Dữ liệu webhook vượt quá giới hạn cho phép."));
        }

        Guid restaurantId;
        if (!string.IsNullOrEmpty(request.RestaurantId))
        {
            if (!Guid.TryParse(request.RestaurantId, out restaurantId) || restaurantId == Guid.Empty)
            {
                throw new RpcException(new Status(StatusCode.InvalidArgument, "Nhà hàng của webhook không hợp lệ."));
            }
        }
        else
        {
            // Legacy URL: untrusted order code is used only for routing, never for updates.
            long unverifiedCode;
            try
            {
                using var document = JsonDocument.Parse(request.Payload, new JsonDocumentOptions
                {
                    MaxDepth = 32
                });
                unverifiedCode = document.RootElement.GetProperty("data").GetProperty("orderCode").GetInt64();
            }
            catch (Exception error) when (error is JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
            {
                throw new RpcException(new Status(StatusCode.InvalidArgument, "Dữ liệu webhook không hợp lệ."));
            }

            var candidates = await Owners(unverifiedCode, context.CancellationToken);
            if (candidates.Count != 1)
            {
                throw new RpcException(new Status(StatusCode.InvalidArgument, "Vui lòng sử dụng đường dẫn webhook riêng của nhà hàng."));
            }

            restaurantId = candidates[0];
        }

        scope.Select(restaurantId);
        var code = provider.VerifyWebhook(request.Payload).OrderCode;
        var owners = await Owners(code, context.CancellationToken);
        if (owners.Count > 1 || (owners.Count == 1 && owners[0] != restaurantId))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Giao dịch không thuộc nhà hàng của webhook."));
        }

        if (owners.Count == 0)
        {
            return new PaymentWebhookReply
            {
                Received = true
            };
        }

        if (!await online.ProcessWebhook(request.Payload, context.CancellationToken))
        {
            await service.Process(request.Payload, context.CancellationToken);
        }

        return new PaymentWebhookReply
        {
            Received = true
        };
    }

    private async Task<List<Guid>> Owners(long code, CancellationToken ct)
    {
        var textCode = code.ToString(CultureInfo.InvariantCulture);
        var owners = await db.OnlinePayments.IgnoreQueryFilters().Where(value => value.ProviderOrderCode == code)
            .Select(value => value.RestaurantId).ToListAsync(ct);
        owners.AddRange(await db.Payments.IgnoreQueryFilters().Where(value => value.ProviderOrderCode == textCode)
            .Select(value => value.Bill.RestaurantId).ToListAsync(ct));
        return owners;
    }
}
