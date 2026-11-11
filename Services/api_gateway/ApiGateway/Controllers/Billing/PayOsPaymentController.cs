using System.Globalization;
using ApiGateway.DTOs.Billing;
using ApiGateway.Settings;
using ApiGateway.Swagger;
using Backend.Contracts.Billing;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Shared.Common.DTOs;
using Shared.Common.Pagination;

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

    [HttpGet("/api/table-sessions/{sessionId:guid}/checkout/{checkoutId:guid}/payments/payos")]
    [ProducesResponseType(typeof(ResponseDTO<PagedResult<WaiterPayOsPaymentDTO>>), 200)]
    public Task<IActionResult> GetForCheckout(Guid sessionId, Guid checkoutId, [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        return Execute(async options =>
        {
            var reply = await client.GetForCheckoutAsync(new WaiterPayOsRequest
            {
                TableSessionId = sessionId.ToString(),
                CheckoutId = checkoutId.ToString(),
                Page = page,
                PageSize = pageSize
            }, options);
            return new PagedResult<WaiterPayOsPaymentDTO>(reply.Items.Select(MapForWaiter).ToArray(),
                reply.TotalCount, reply.Page, reply.PageSize);
        }, ct);
    }

    [HttpGet("/api/table-sessions/{sessionId:guid}/checkout/{checkoutId:guid}/payments/{paymentId:guid}/payos")]
    [ProducesResponseType(typeof(ResponseDTO<WaiterPayOsPaymentDTO>), 200)]
    public Task<IActionResult> GetForWaiter(Guid sessionId, Guid checkoutId, Guid paymentId, CancellationToken ct)
    {
        return Execute(async options => MapForWaiter(await client.GetForWaiterAsync(new WaiterPayOsRequest
        {
            TableSessionId = sessionId.ToString(),
            CheckoutId = checkoutId.ToString(),
            PaymentId = paymentId.ToString()
        }, options)), ct);
    }

    private static WaiterPayOsPaymentDTO MapForWaiter(WaiterPayOsInfo value)
    {
        return new WaiterPayOsPaymentDTO
        {
            PaymentId = Guid.Parse(value.PaymentId),
            BillId = Guid.Parse(value.BillId),
            TableSessionId = Guid.Parse(value.TableSessionId),
            CheckoutId = Guid.Parse(value.CheckoutId),
            Status = value.Status,
            BillStatus = value.BillStatus,
            Amount = decimal.Parse(value.Amount, CultureInfo.InvariantCulture),
            CheckoutUrl = value.HasCheckoutUrl ? value.CheckoutUrl : null,
            QrCode = value.HasQrCode ? value.QrCode : null,
            ExpiresAt = value.HasExpiresAt ? DateTimeOffset.Parse(value.ExpiresAt, CultureInfo.InvariantCulture) : null,
            PaidAt = value.HasPaidAt ? DateTimeOffset.Parse(value.PaidAt, CultureInfo.InvariantCulture) : null,
            CanPresentQr = value.CanPresentQr,
            PaymentConfirmed = value.PaymentConfirmed,
            RequiresReview = value.RequiresReview,
            EvaluatedAt = DateTimeOffset.Parse(value.EvaluatedAt, CultureInfo.InvariantCulture)
        };
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
