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
        return MapPage(await service.GetIncoming(Id(request.RestaurantId), Query(request), context.CancellationToken));
    }

    public override async Task<PaymentSubmissionPage> GetPaid(PaymentSubmissionListRequest request, ServerCallContext context)
    {
        return MapPage(await service.GetPaid(Id(request.RestaurantId), Query(request), context.CancellationToken));
    }

    private static PaymentSubmissionQueryDTO Query(PaymentSubmissionListRequest request)
    {
        return new PaymentSubmissionQueryDTO
        {
            Page = request.Page,
            PageSize = request.PageSize,
            Search = request.HasSearch ? request.Search : null,
            AreaId = request.HasAreaId ? Id(request.AreaId) : null,
            CashShiftId = request.HasCashShiftId ? Id(request.CashShiftId) : null
        };
    }

    private static PaymentSubmissionPage MapPage(PaymentSubmissionPageDTO result)
    {
        var reply = new PaymentSubmissionPage
        {
            TotalCount = result.TotalCount,
            Page = result.Page,
            PageSize = result.PageSize,
            KnownTotalAmount = result.KnownTotalAmount.ToString(CultureInfo.InvariantCulture),
            UnbilledCount = result.UnbilledCount,
            CashShiftId = result.CashShiftId.ToString()
        };
        if (result.TotalAmount.HasValue)
        {
            reply.TotalAmount = result.TotalAmount.Value.ToString(CultureInfo.InvariantCulture);
        }

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

        if (value.BillCode is not null)
        {
            reply.BillCode = value.BillCode;
        }

        if (value.TotalAmount.HasValue)
        {
            reply.TotalAmount = value.TotalAmount.Value.ToString(CultureInfo.InvariantCulture);
        }

        if (value.TableId.HasValue)
        {
            reply.TableId = value.TableId.Value.ToString();
            reply.TableCode = value.TableCode!;
        }

        if (value.AreaId.HasValue)
        {
            reply.AreaId = value.AreaId.Value.ToString();
            reply.AreaName = value.AreaName!;
        }

        reply.PartySize = value.PartySize;
        if (value.OpenedAt.HasValue)
        {
            reply.OpenedAt = value.OpenedAt.Value.ToString("O", CultureInfo.InvariantCulture);
        }

        if (value.PaidAt.HasValue)
        {
            reply.PaidAt = value.PaidAt.Value.ToString("O", CultureInfo.InvariantCulture);
        }

        reply.Tables.AddRange(value.Tables.Select(table => new CashierTableInfo
        {
            TableId = table.TableId.ToString(),
            TableCode = table.TableCode,
            AreaId = table.AreaId.ToString(),
            AreaName = table.AreaName
        }));

        return reply;
    }
}
