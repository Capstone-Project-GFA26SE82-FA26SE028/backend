using System.Globalization;
using Backend.Contracts.Billing;
using Billing.BLL.Interfaces;
using Billing.Common.DTOs;
using Billing.Common.Exceptions;
using Grpc.Core;

namespace Billing.API.GrpcServices;

public sealed class PaymentGrpcService(IPaymentService service) : PaymentGrpc.PaymentGrpcBase
{
    public override async Task<CashPaymentReply> PayCash(PayCashRequest request, ServerCallContext context)
    {
        if (!request.HasCashReceived || !decimal.TryParse(request.CashReceived,
            NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var received))
        {
            throw new BillingException(400, "Provide cash received as an invariant decimal number.");
        }

        var result = await service.PayCash(Id(request.BillId), request.IdempotencyKey, new CreateCashPaymentDTO
        {
            CheckoutId = Id(request.CheckoutId),
            CashShiftId = Id(request.CashShiftId),
            CashReceived = received
        }, context.CancellationToken);
        return new CashPaymentReply
        {
            Payment = Map(result.Payment),
            Created = result.Created
        };
    }

    public override async Task<PaymentInfo> GetById(PaymentIdRequest request, ServerCallContext context)
    {
        return Map(await service.GetById(Id(request.PaymentId), context.CancellationToken));
    }

    private static Guid Id(string value)
    {
        if (!Guid.TryParse(value, out var id) || id == Guid.Empty)
        {
            throw new BillingException(400, "A valid ID is required.");
        }

        return id;
    }

    private static PaymentInfo Map(PaymentDTO value)
    {
        return new PaymentInfo
        {
            PaymentId = value.PaymentId.ToString(),
            BillId = value.BillId.ToString(),
            BillNumber = value.BillNumber,
            RestaurantId = value.RestaurantId.ToString(),
            CheckoutId = value.CheckoutId.ToString(),
            CashShiftId = value.CashShiftId.ToString(),
            CashRegisterId = value.CashRegisterId.ToString(),
            MethodCode = value.MethodCode,
            Status = value.Status,
            Currency = value.Currency,
            Amount = value.Amount.ToString(CultureInfo.InvariantCulture),
            CashReceived = value.CashReceived.ToString(CultureInfo.InvariantCulture),
            ChangeAmount = value.ChangeAmount.ToString(CultureInfo.InvariantCulture),
            ConfirmedByRef = value.ConfirmedByRef.ToString(),
            PaidAt = value.PaidAt.ToString("O", CultureInfo.InvariantCulture),
            CashMovementId = value.CashMovementId.ToString()
        };
    }
}
