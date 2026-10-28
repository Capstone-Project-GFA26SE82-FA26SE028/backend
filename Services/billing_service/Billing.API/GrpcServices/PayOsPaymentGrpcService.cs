using System.Globalization;
using Backend.Contracts.Billing;
using Billing.BLL.Interfaces;
using Billing.Common.DTOs;
using Billing.Common.Exceptions;
using Grpc.Core;

namespace Billing.API.GrpcServices;

public sealed class PayOsPaymentGrpcService(IPayOsPaymentService service) : PayOsPaymentGrpc.PayOsPaymentGrpcBase
{
    public override async Task<PayOsPaymentReply> Create(CreatePayOsPaymentRequest request, ServerCallContext context)
    {
        var result = await service.Create(Id(request.BillId), request.IdempotencyKey, new CreatePayOsPaymentDTO
        {
            CheckoutId = Id(request.CheckoutId),
            CashShiftId = Id(request.CashShiftId)
        }, context.CancellationToken);
        return new PayOsPaymentReply
        {
            Payment = Map(result.Payment),
            Created = result.Created
        };
    }

    public override async Task<PayOsPaymentInfo> GetById(PaymentIdRequest request, ServerCallContext context)
    {
        return Map(await service.GetById(Id(request.PaymentId), context.CancellationToken));
    }

    public override async Task<PayOsPaymentInfo> Reconcile(PaymentIdRequest request, ServerCallContext context)
    {
        return Map(await service.Reconcile(Id(request.PaymentId), context.CancellationToken));
    }

    public override async Task<PayOsPaymentInfo> Cancel(PaymentIdRequest request, ServerCallContext context)
    {
        return Map(await service.Cancel(Id(request.PaymentId), context.CancellationToken));
    }

    private static Guid Id(string value)
    {
        if (!Guid.TryParse(value, out var id) || id == Guid.Empty)
        {
            throw new BillingException(400, "A valid ID is required.");
        }

        return id;
    }

    private static PayOsPaymentInfo Map(PayOsPaymentDTO value)
    {
        var result = new PayOsPaymentInfo
        {
            PaymentId = value.PaymentId.ToString(),
            BillId = value.BillId.ToString(),
            CashShiftId = value.CashShiftId.ToString(),
            RestaurantId = value.RestaurantId.ToString(),
            Status = value.Status,
            Amount = value.Amount.ToString(CultureInfo.InvariantCulture),
            IsVerifiedZero = value.IsVerifiedZero,
            RequiresReview = value.RequiresReview,
            LinkDetailsAvailable = value.LinkDetailsAvailable
        };
        if (value.ProviderOrderCode is not null)
        {
            result.ProviderOrderCode = value.ProviderOrderCode;
        }

        if (value.ProviderPaymentLinkId is not null)
        {
            result.ProviderPaymentLinkId = value.ProviderPaymentLinkId;
        }

        if (value.CheckoutUrl is not null)
        {
            result.CheckoutUrl = value.CheckoutUrl;
        }

        if (value.QrCode is not null)
        {
            result.QrCode = value.QrCode;
        }

        if (value.ProviderTransactionId is not null)
        {
            result.ProviderTransactionId = value.ProviderTransactionId;
        }

        if (value.ExpiresAt.HasValue)
        {
            result.ExpiresAt = value.ExpiresAt.Value.ToString("O", CultureInfo.InvariantCulture);
        }

        if (value.PaidAt.HasValue)
        {
            result.PaidAt = value.PaidAt.Value.ToString("O", CultureInfo.InvariantCulture);
        }

        if (value.ProviderAmountPaid.HasValue)
        {
            result.ProviderAmountPaid = value.ProviderAmountPaid.Value;
        }

        if (value.ProviderAmountRemaining.HasValue)
        {
            result.ProviderAmountRemaining = value.ProviderAmountRemaining.Value;
        }

        return result;
    }
}
