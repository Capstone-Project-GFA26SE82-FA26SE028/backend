using Backend.Contracts.Billing;
using Billing.BLL.Interfaces;
using Grpc.Core;
using System.Globalization;
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

        // The platform owns the shared PayOS channel. Verify before routing to an owner.
        var code = provider.VerifyWebhook(request.Payload).OrderCode;
        var textCode = code.ToString(CultureInfo.InvariantCulture);
        var owners = await db.OnlinePayments.IgnoreQueryFilters().Where(value => value.ProviderOrderCode == code)
            .Select(value => value.RestaurantId).ToListAsync(context.CancellationToken);
        owners.AddRange(await db.Payments.IgnoreQueryFilters().Where(value => value.ProviderOrderCode == textCode)
            .Select(value => value.Bill.RestaurantId).ToListAsync(context.CancellationToken));
        if (owners.Count == 0)
        {
            // A signed provider probe/unknown event does not belong to a restaurant.
            // Acknowledge it without creating or modifying tenant data.
            return new PaymentWebhookReply
            {
                Received = true
            };
        }

        if (owners.Count != 1)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Không xác định được nhà hàng của giao dịch."));
        }

        scope.Select(owners[0]);
        if (!await online.ProcessWebhook(request.Payload, context.CancellationToken))
        {
            await service.Process(request.Payload, context.CancellationToken);
        }
        return new PaymentWebhookReply
        {
            Received = true
        };
    }
}
