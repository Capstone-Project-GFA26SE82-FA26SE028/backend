using System.Globalization;
using ApiGateway.DTOs.Billing;
using ApiGateway.Settings;
using ApiGateway.Swagger;
using Backend.Contracts.Billing;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Shared.Common.DTOs;

namespace ApiGateway.Controllers.Billing;

[ApiController]
[Route("api/payments")]
[RequireBearerToken]
public sealed class PayOsPaymentController(PayOsPaymentGrpc.PayOsPaymentGrpcClient client, IOptions<PayOsGatewaySettings> settings)
    : BillingControllerBase(Options.Create(new BillingClientSettings
    {
        TimeoutSeconds = settings.Value.TimeoutSeconds
    }))
{
    [HttpPost("/api/bills/{billId:guid}/payments/payos")]
    [ProducesResponseType(typeof(ResponseDTO<PayOsPaymentDTO>), 201)]
    [ProducesResponseType(typeof(ResponseDTO<PayOsPaymentDTO>), 200)]
    public Task<IActionResult> Create(Guid billId, CreatePayOsPaymentDTO body,
        [FromHeader(Name = "Idempotency-Key")] string key, CancellationToken ct)
    {
        return ExecuteWithStatus(async options =>
        {
            var reply = await client.CreateAsync(new CreatePayOsPaymentRequest
            {
                BillId = billId.ToString(),
                CheckoutId = body.CheckoutId.ToString(),
                CashShiftId = body.CashShiftId.ToString(),
                IdempotencyKey = key
            }, options);
            if (reply.Created)
            {
                Response.Headers.Location = $"/api/payments/{reply.Payment.PaymentId}/payos";
            }

            return (Map(reply.Payment), reply.Created ? 201 : 200);
        }, ct);
    }

    [HttpGet("{id:guid}/payos")]
    [ProducesResponseType(typeof(ResponseDTO<PayOsPaymentDTO>), 200)]
    public Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        return Execute(async options => Map(await client.GetByIdAsync(RequestId(id), options)), ct);
    }

    [HttpPost("{id:guid}/payos/reconcile")]
    [ProducesResponseType(typeof(ResponseDTO<PayOsPaymentDTO>), 200)]
    public Task<IActionResult> Reconcile(Guid id, CancellationToken ct)
    {
        return Execute(async options => Map(await client.ReconcileAsync(RequestId(id), options)), ct);
    }

    [HttpPost("{id:guid}/payos/cancel")]
    [ProducesResponseType(typeof(ResponseDTO<PayOsPaymentDTO>), 200)]
    public Task<IActionResult> Cancel(Guid id, CancellationToken ct)
    {
        return Execute(async options => Map(await client.CancelAsync(RequestId(id), options)), ct);
    }

    private static PaymentIdRequest RequestId(Guid id)
    {
        return new PaymentIdRequest
        {
            PaymentId = id.ToString()
        };
    }

    private static PayOsPaymentDTO Map(PayOsPaymentInfo value)
    {
        return new PayOsPaymentDTO
        {
            PaymentId = Guid.Parse(value.PaymentId),
            BillId = Guid.Parse(value.BillId),
            CashShiftId = Guid.Parse(value.CashShiftId),
            RestaurantId = Guid.Parse(value.RestaurantId),
            Status = value.Status,
            Amount = decimal.Parse(value.Amount, CultureInfo.InvariantCulture),
            ProviderOrderCode = value.HasProviderOrderCode ? value.ProviderOrderCode : null,
            ProviderPaymentLinkId = value.HasProviderPaymentLinkId ? value.ProviderPaymentLinkId : null,
            CheckoutUrl = value.HasCheckoutUrl ? value.CheckoutUrl : null,
            QrCode = value.HasQrCode ? value.QrCode : null,
            ProviderTransactionId = value.HasProviderTransactionId ? value.ProviderTransactionId : null,
            ExpiresAt = value.HasExpiresAt ? DateTimeOffset.Parse(value.ExpiresAt, CultureInfo.InvariantCulture) : null,
            PaidAt = value.HasPaidAt ? DateTimeOffset.Parse(value.PaidAt, CultureInfo.InvariantCulture) : null,
            ProviderAmountPaid = value.HasProviderAmountPaid ? value.ProviderAmountPaid : null,
            ProviderAmountRemaining = value.HasProviderAmountRemaining ? value.ProviderAmountRemaining : null,
            IsVerifiedZero = value.IsVerifiedZero,
            RequiresReview = value.RequiresReview,
            LinkDetailsAvailable = value.LinkDetailsAvailable
        };
    }
}
