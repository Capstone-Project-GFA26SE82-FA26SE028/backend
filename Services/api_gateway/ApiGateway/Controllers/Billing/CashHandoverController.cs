using System.Globalization;
using ApiGateway.DTOs.Billing;
using ApiGateway.Settings;
using ApiGateway.Swagger;
using Backend.Contracts.Billing;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Shared.Common.Pagination;

namespace ApiGateway.Controllers.Billing;

[ApiController]
[Route("api/cash-handovers")]
[RequireBearerToken]
public sealed class CashHandoverController(CashHandoverGrpc.CashHandoverGrpcClient client,
    IOptions<BillingClientSettings> settings) : BillingControllerBase(settings)
{
    [HttpPost("/api/bills/{billId:guid}/cash-handovers")]
    public Task<IActionResult> Create(Guid billId, [FromHeader(Name = "Idempotency-Key")] string key,
        CreateCashHandoverDTO body, CancellationToken ct)
    {
        return ExecuteWithStatus(async options =>
        {
            var request = new CreateCashHandoverRequest
            {
                BillId = billId.ToString(),
                CheckoutId = body.CheckoutId.ToString(),
                IdempotencyKey = key
            };
            if (body.CashReceived.HasValue)
            {
                request.CashReceived = body.CashReceived.Value.ToString(CultureInfo.InvariantCulture);
            }

            var reply = await client.CreateAsync(request, options);
            if (reply.Created)
            {
                Response.Headers.Location = $"/api/cash-handovers/{reply.Handover.PaymentId}";
            }

            return (Map(reply.Handover), reply.Created ? 201 : 200);
        }, ct);
    }

    [HttpGet("{paymentId:guid}")]
    public Task<IActionResult> GetById(Guid paymentId, CancellationToken ct)
    {
        return Execute(async options => Map(await client.GetByIdAsync(new PaymentIdRequest
        {
            PaymentId = paymentId.ToString()
        }, options)), ct);
    }

    [HttpPost("{paymentId:guid}/confirm")]
    public Task<IActionResult> Confirm(Guid paymentId, ConfirmCashHandoverDTO body, CancellationToken ct)
    {
        return Execute(async options =>
        {
            var request = new ConfirmCashHandoverRequest
            {
                PaymentId = paymentId.ToString()
            };
            if (body.ConfirmedAmount.HasValue)
            {
                request.ConfirmedAmount = body.ConfirmedAmount.Value.ToString(CultureInfo.InvariantCulture);
            }

            return Map(await client.ConfirmAsync(request, options));
        }, ct);
    }

    [HttpGet("/api/cash-registers/{cashRegisterId:guid}/cash-handovers")]
    public Task<IActionResult> GetAll(Guid cashRegisterId, [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        return Execute(async options =>
        {
            var reply = await client.GetAllAsync(new CashHandoverListRequest
            {
                CashRegisterId = cashRegisterId.ToString(),
                Page = page,
                PageSize = pageSize
            }, options);
            return new PagedResult<CashHandoverDTO>(reply.Items.Select(Map).ToArray(), reply.TotalCount, reply.Page, reply.PageSize);
        }, ct);
    }

    private static CashHandoverDTO Map(CashHandoverInfo value)
    {
        return new CashHandoverDTO
        {
            SessionClosureConfirmed = value.HasSessionClosureConfirmed ? value.SessionClosureConfirmed : null,
            PaymentId = Guid.Parse(value.PaymentId),
            BillId = Guid.Parse(value.BillId),
            RestaurantId = Guid.Parse(value.RestaurantId),
            CashRegisterId = Guid.Parse(value.CashRegisterId),
            CashShiftId = Guid.Parse(value.CashShiftId),
            WaiterUserRef = Guid.Parse(value.WaiterUserRef),
            CashierUserRef = value.HasCashierUserRef ? Guid.Parse(value.CashierUserRef) : null,
            Amount = decimal.Parse(value.Amount, CultureInfo.InvariantCulture),
            CashReceived = decimal.Parse(value.CashReceived, CultureInfo.InvariantCulture),
            ChangeAmount = decimal.Parse(value.ChangeAmount, CultureInfo.InvariantCulture),
            ConfirmedAmount = value.HasConfirmedAmount ? decimal.Parse(value.ConfirmedAmount, CultureInfo.InvariantCulture) : null,
            Status = value.Status,
            PaymentStatus = value.PaymentStatus,
            CollectedAt = DateTimeOffset.Parse(value.CollectedAt, CultureInfo.InvariantCulture),
            ConfirmedAt = value.HasConfirmedAt ? DateTimeOffset.Parse(value.ConfirmedAt, CultureInfo.InvariantCulture) : null
        };
    }

}
