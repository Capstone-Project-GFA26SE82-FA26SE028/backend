using System.Globalization;
using Backend.Contracts.Billing;
using Billing.BLL.Interfaces;
using Billing.Common.DTOs;
using Billing.Common.Exceptions;
using Grpc.Core;

namespace Billing.API.GrpcServices;

public sealed class CashHandoverGrpcService(ICashHandoverService service) : CashHandoverGrpc.CashHandoverGrpcBase
{
    public override async Task<CashHandoverReply> Create(CreateCashHandoverRequest request, ServerCallContext context)
    {
        var result = await service.Create(Id(request.BillId), request.IdempotencyKey, new CreateCashHandoverDTO
        {
            CheckoutId = Id(request.CheckoutId),
            CashReceived = Money(request.HasCashReceived, request.CashReceived)
        }, context.CancellationToken);
        return new CashHandoverReply
        {
            Handover = Map(result.Handover),
            Created = result.Created
        };
    }

    public override async Task<CashHandoverInfo> GetById(PaymentIdRequest request, ServerCallContext context)
    {
        return Map(await service.GetById(Id(request.PaymentId), context.CancellationToken));
    }

    public override async Task<CashHandoverInfo> Confirm(ConfirmCashHandoverRequest request, ServerCallContext context)
    {
        return Map(await service.Confirm(Id(request.PaymentId), Money(request.HasConfirmedAmount, request.ConfirmedAmount), context.CancellationToken));
    }

    public override async Task<CashHandoverPage> GetAll(CashHandoverListRequest request, ServerCallContext context)
    {
        var result = await service.GetAll(Id(request.CashRegisterId), request.Page, request.PageSize, context.CancellationToken);
        var page = new CashHandoverPage
        {
            TotalCount = result.TotalCount,
            Page = result.Page,
            PageSize = result.PageSize
        };
        page.Items.AddRange(result.Items.Select(Map));
        return page;
    }

    private static Guid Id(string value)
    {
        if (!Guid.TryParse(value, out var id) || id == Guid.Empty)
        {
            throw new BillingException(400, "Yêu cầu ID hợp lệ.");
        }

        return id;
    }

    private static decimal Money(bool present, string value)
    {
        if (!present || !decimal.TryParse(value, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign,
            CultureInfo.InvariantCulture, out var amount))
        {
            throw new BillingException(400, "Nhập rõ số tiền VND bằng số thập phân hợp lệ.");
        }

        return amount;
    }

    private static CashHandoverInfo Map(CashHandoverDTO value)
    {
        var result = new CashHandoverInfo
        {
            PaymentId = value.PaymentId.ToString(),
            BillId = value.BillId.ToString(),
            RestaurantId = value.RestaurantId.ToString(),
            CashRegisterId = value.CashRegisterId.ToString(),
            CashShiftId = value.CashShiftId.ToString(),
            WaiterUserRef = value.WaiterUserRef.ToString(),
            Amount = value.Amount.ToString(CultureInfo.InvariantCulture),
            CashReceived = value.CashReceived.ToString(CultureInfo.InvariantCulture),
            ChangeAmount = value.ChangeAmount.ToString(CultureInfo.InvariantCulture),
            Status = value.Status,
            PaymentStatus = value.PaymentStatus,
            CollectedAt = value.CollectedAt.ToString("O")
        };
        if (value.CashierUserRef.HasValue)
        {
            result.CashierUserRef = value.CashierUserRef.Value.ToString();
        }

        if (value.ConfirmedAmount.HasValue)
        {
            result.ConfirmedAmount = value.ConfirmedAmount.Value.ToString(CultureInfo.InvariantCulture);
        }

        if (value.ConfirmedAt.HasValue)
        {
            result.ConfirmedAt = value.ConfirmedAt.Value.ToString("O");
        }

        if (value.SessionClosureConfirmed.HasValue)
        {
            result.SessionClosureConfirmed = value.SessionClosureConfirmed.Value;
        }

        return result;
    }
}
