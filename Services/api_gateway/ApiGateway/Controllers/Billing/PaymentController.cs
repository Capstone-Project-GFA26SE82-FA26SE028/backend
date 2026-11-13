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
public sealed class PaymentController(PaymentGrpc.PaymentGrpcClient client, IOptions<BillingClientSettings> settings)
    : BillingControllerBase(settings)
{
    [HttpPost("/api/bills/{billId:guid}/payments/cash")]
    [ProducesResponseType(typeof(ResponseDTO<PaymentDTO>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ResponseDTO<PaymentDTO>), StatusCodes.Status200OK)]
    public Task<IActionResult> PayCash(Guid billId, CreateCashPaymentDTO body,
        [FromHeader(Name = "Idempotency-Key")] string key, CancellationToken ct)
    {
        return ExecuteWithStatus(async options =>
        {
            var request = new PayCashRequest
            {
                BillId = billId.ToString(),
                CheckoutId = body.CheckoutId.ToString(),
                CashShiftId = body.CashShiftId.ToString(),
                IdempotencyKey = key
            };
            if (body.CashReceived.HasValue)
            {
                request.CashReceived = body.CashReceived.Value.ToString(CultureInfo.InvariantCulture);
            }

            var reply = await client.PayCashAsync(request, options);
            if (reply.Created)
            {
                Response.Headers.Location = $"/api/payments/{reply.Payment.PaymentId}";
            }

            return (Map(reply.Payment), reply.Created ? 201 : 200);
        }, ct);
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(ResponseDTO<PaymentDTO>), StatusCodes.Status200OK)]
    public Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        return Execute(async options => Map(await client.GetByIdAsync(new PaymentIdRequest
        {
            PaymentId = id.ToString()
        }, options)), ct);
    }

    private static PaymentDTO Map(PaymentInfo value)
    {
        return new PaymentDTO
        {
            SessionClosureConfirmed = value.HasSessionClosureConfirmed ? value.SessionClosureConfirmed : null,
            PaymentId = Guid.Parse(value.PaymentId),
            BillId = Guid.Parse(value.BillId),
            BillNumber = value.BillNumber,
            RestaurantId = Guid.Parse(value.RestaurantId),
            CheckoutId = Guid.Parse(value.CheckoutId),
            CashShiftId = Guid.Parse(value.CashShiftId),
            CashRegisterId = Guid.Parse(value.CashRegisterId),
            MethodCode = value.MethodCode,
            Status = value.Status,
            Currency = value.Currency,
            Amount = decimal.Parse(value.Amount, CultureInfo.InvariantCulture),
            CashReceived = decimal.Parse(value.CashReceived, CultureInfo.InvariantCulture),
            ChangeAmount = decimal.Parse(value.ChangeAmount, CultureInfo.InvariantCulture),
            ConfirmedByRef = Guid.Parse(value.ConfirmedByRef),
            PaidAt = DateTimeOffset.Parse(value.PaidAt, CultureInfo.InvariantCulture),
            CashMovementId = Guid.Parse(value.CashMovementId)
        };
    }
}
