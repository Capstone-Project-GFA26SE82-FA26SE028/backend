using Backend.Contracts.Billing;
using Billing.BLL.Interfaces;
using Grpc.Core;

namespace Billing.API.GrpcServices;

public sealed class PaymentWebhookGrpcService(IPaymentWebhookService service) : PaymentWebhookGrpc.PaymentWebhookGrpcBase
{
    public override async Task<PaymentWebhookReply> Process(PaymentWebhookRequest request, ServerCallContext context)
    {
        await service.Process(request.Payload, context.CancellationToken);
        return new PaymentWebhookReply
        {
            Received = true
        };
    }
}
