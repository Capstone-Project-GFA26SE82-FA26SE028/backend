using System.Globalization;
using Backend.Contracts.Billing;
using Billing.BLL.Interfaces;
using Billing.Common.DTOs;
using Grpc.Core;

namespace Billing.API.GrpcServices;

public sealed class PaymentSubmissionGrpcService(IPaymentSubmissionService service) : PaymentSubmissionGrpc.PaymentSubmissionGrpcBase
{
    public override async Task<PaymentSubmissionInfo> Submit(SubmitPaymentRequest request, ServerCallContext context)
    {
        return Map(await service.Submit(Id(request.TableSessionId), Id(request.ServiceRequestId), context.CancellationToken));
    }

    public override async Task<PaymentSubmissionPage> GetIncoming(PaymentSubmissionListRequest request, ServerCallContext context)
    {
        var result = await service.GetIncoming(Id(request.RestaurantId), request.Page, request.PageSize, context.CancellationToken);
        var reply = new PaymentSubmissionPage
        {
            TotalCount = result.TotalCount,
            Page = result.Page,
            PageSize = result.PageSize
        };
        reply.Items.AddRange(result.Items.Select(Map));
        return reply;
    }

    private static Guid Id(string value)
    {
        if (!Guid.TryParse(value, out var id) || id == Guid.Empty)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Yêu cầu ID hợp lệ."));
        }

        return id;
    }

    private static PaymentSubmissionInfo Map(PaymentSubmissionDTO value)
    {
        var reply = new PaymentSubmissionInfo
        {
            TableSessionId = value.TableSessionId.ToString(),
            CashRegisterId = value.CashRegisterId.ToString(),
            Status = value.Status
        };
        if (value.CheckoutId.HasValue)
        {
            reply.CheckoutId = value.CheckoutId.Value.ToString();
        }

        if (value.SubmittedAt.HasValue)
        {
            reply.SubmittedAt = value.SubmittedAt.Value.ToString("O", CultureInfo.InvariantCulture);
        }

        if (value.BillId.HasValue)
        {
            reply.BillId = value.BillId.Value.ToString();
        }

        return reply;
    }
}
