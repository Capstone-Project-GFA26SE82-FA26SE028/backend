using System.Globalization;
using Backend.Contracts.Billing;
using Billing.BLL.Interfaces;
using Billing.Common.DTOs;
using Billing.Common.Exceptions;
using Grpc.Core;

namespace Billing.API.GrpcServices;

public sealed class CashMovementGrpcService(ICashMovementService service) : CashMovementGrpc.CashMovementGrpcBase
{
    public override async Task<CashMovementReply> Create(CreateCashMovementRequest request, ServerCallContext context)
    {
        if (!request.HasAmount || !decimal.TryParse(request.Amount,
            NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var amount))
        {
            throw new BillingException(400, "Vui lòng nhập số tiền mặt dưới dạng số thập phân dùng dấu chấm phân cách.");
        }

        var result = await service.Create(Id(request.CashShiftId), request.IdempotencyKey, new CreateCashMovementDTO
        {
            Type = request.Type,
            Amount = amount,
            Reason = request.Reason
        }, context.CancellationToken);
        return new CashMovementReply
        {
            Movement = Map(result.Movement),
            Created = result.Created
        };
    }

    public override async Task<CashLedgerInfo> GetAll(CashLedgerRequest request, ServerCallContext context)
    {
        var result = await service.GetAll(Id(request.CashShiftId), request.Page, request.PageSize, context.CancellationToken);
        var reply = new CashLedgerInfo
        {
            CashShiftId = result.CashShiftId.ToString(),
            RestaurantId = result.RestaurantId.ToString(),
            Currency = result.Currency,
            OpeningCash = result.OpeningCash.ToString(CultureInfo.InvariantCulture),
            MovementTotal = result.MovementTotal.ToString(CultureInfo.InvariantCulture),
            ExpectedCash = result.ExpectedCash.ToString(CultureInfo.InvariantCulture),
            TotalCount = result.Movements.TotalCount,
            Page = result.Movements.Page,
            PageSize = result.Movements.PageSize
        };
        reply.Items.AddRange(result.Movements.Items.Select(Map));
        return reply;
    }

    public override async Task<CashMovementInfo> GetById(CashMovementIdRequest request, ServerCallContext context)
    {
        return Map(await service.GetById(Id(request.CashMovementId), context.CancellationToken));
    }

    private static Guid Id(string value)
    {
        if (!Guid.TryParse(value, out var id) || id == Guid.Empty)
        {
            throw new BillingException(400, "Yêu cầu ID hợp lệ.");
        }

        return id;
    }

    private static CashMovementInfo Map(CashMovementDTO value)
    {
        var result = new CashMovementInfo
        {
            CashMovementId = value.CashMovementId.ToString(),
            CashShiftId = value.CashShiftId.ToString(),
            RestaurantId = value.RestaurantId.ToString(),
            Type = value.Type,
            SignedAmount = value.SignedAmount.ToString(CultureInfo.InvariantCulture),
            PostedAt = value.PostedAt.ToString("O", CultureInfo.InvariantCulture),
            PerformerSource = value.PerformerSource
        };
        if (value.PaymentId.HasValue)
        {
            result.PaymentId = value.PaymentId.Value.ToString();
        }

        if (value.Reason is not null)
        {
            result.Reason = value.Reason;
        }

        if (value.PerformedByRef.HasValue)
        {
            result.PerformedByRef = value.PerformedByRef.Value.ToString();
        }

        return result;
    }
}
