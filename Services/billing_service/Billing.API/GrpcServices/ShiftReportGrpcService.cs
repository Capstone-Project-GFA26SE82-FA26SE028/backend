using System.Globalization;
using Backend.Contracts.Billing;
using Billing.BLL.Interfaces;
using Billing.Common.DTOs;
using Billing.Common.Exceptions;
using Grpc.Core;

namespace Billing.API.GrpcServices;

public sealed class ShiftReportGrpcService(IShiftReportService service) : ShiftReportGrpc.ShiftReportGrpcBase
{
    public override async Task<ShiftReconciliationInfo> Preview(ShiftReportShiftRequest request, ServerCallContext context)
    {
        var value = await service.Preview(Id(request.CashShiftId), context.CancellationToken);
        var reply = new ShiftReconciliationInfo
        {
            CashShiftId = value.CashShiftId.ToString(),
            RestaurantId = value.RestaurantId.ToString(),
            Status = value.Status,
            OpeningCash = Money(value.OpeningCash),
            MovementTotal = Money(value.MovementTotal),
            ExpectedCash = Money(value.ExpectedCash),
            SaleTotal = Money(value.SaleTotal),
            CashInTotal = Money(value.CashInTotal),
            CashOutTotal = Money(value.CashOutTotal),
            PaymentCount = value.PaymentCount,
            CanClose = value.CanClose
        };
        foreach (var blocker in value.Blockers)
        {
            var item = new ReconciliationBlockerInfo
            {
                Code = blocker.Code,
                Message = blocker.Message
            };
            if (blocker.PaymentId.HasValue)
            {
                item.PaymentId = blocker.PaymentId.Value.ToString();
            }

            if (blocker.CashMovementId.HasValue)
            {
                item.CashMovementId = blocker.CashMovementId.Value.ToString();
            }

            reply.Blockers.Add(item);
        }

        return reply;
    }

    public override async Task<CloseCashShiftReply> Close(CloseCashShiftRequest request, ServerCallContext context)
    {
        var result = await service.Close(Id(request.CashShiftId), new CloseCashShiftDTO
        {
            ActualCash = Amount(request.HasActualCash, request.ActualCash),
            CashHandleOver = Amount(request.HasCashHandleOver, request.CashHandleOver),
            DifferenceReason = request.DifferenceReason
        }, context.CancellationToken);
        return new CloseCashShiftReply
        {
            Report = Map(result.Report),
            Created = result.Created
        };
    }

    public override async Task<ShiftReportInfo> GetById(ShiftReportIdRequest request, ServerCallContext context)
    {
        return Map(await service.GetById(Id(request.ShiftReportId), context.CancellationToken));
    }

    public override async Task<ShiftReportInfo> GetForShift(ShiftReportShiftRequest request, ServerCallContext context)
    {
        return Map(await service.GetForShift(Id(request.CashShiftId), context.CancellationToken));
    }

    public override async Task<ShiftReportPage> GetAll(ShiftReportListRequest request, ServerCallContext context)
    {
        var result = await service.GetAll(request.HasRestaurantId ? Id(request.RestaurantId) : null, request.Page,
            request.PageSize, context.CancellationToken);
        var reply = new ShiftReportPage
        {
            TotalCount = result.TotalCount,
            Page = result.Page,
            PageSize = result.PageSize
        };
        reply.Items.AddRange(result.Items.Select(Map));
        return reply;
    }

    private static decimal Amount(bool present, string value)
    {
        if (!present || !decimal.TryParse(value, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture, out var amount))
        {
            throw new BillingException(400, "Provide both actual cash and cash handed over as invariant decimal amounts.");
        }

        return amount;
    }

    private static string Money(decimal value)
    {
        return value.ToString(CultureInfo.InvariantCulture);
    }

    private static Guid Id(string value)
    {
        if (!Guid.TryParse(value, out var id) || id == Guid.Empty)
        {
            throw new BillingException(400, "A valid ID is required.");
        }

        return id;
    }

    private static ShiftReportInfo Map(ShiftReportDTO value)
    {
        var reply = new ShiftReportInfo
        {
            ShiftReportId = value.ShiftReportId.ToString(),
            CashShiftId = value.CashShiftId.ToString(),
            CashRegisterId = value.CashRegisterId.ToString(),
            RestaurantId = value.RestaurantId.ToString(),
            CashierUserRef = value.CashierUserRef.ToString(),
            PerformerSource = value.PerformerSource,
            ExpectedCash = Money(value.ExpectedCash),
            ActualCash = Money(value.ActualCash),
            CashHandleOver = Money(value.CashHandleOver),
            Difference = Money(value.Difference),
            RemainingCash = Money(value.RemainingCash),
            ClosedAt = value.ClosedAt.ToString("O", CultureInfo.InvariantCulture)
        };
        if (value.DifferenceReason is not null)
        {
            reply.DifferenceReason = value.DifferenceReason;
        }

        return reply;
    }
}
