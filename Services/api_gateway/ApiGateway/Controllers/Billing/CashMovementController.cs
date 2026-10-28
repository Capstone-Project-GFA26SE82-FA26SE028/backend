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
[Route("api/cash-movements")]
[RequireBearerToken]
public sealed class CashMovementController(CashMovementGrpc.CashMovementGrpcClient client,
    IOptions<BillingClientSettings> settings) : BillingControllerBase(settings)
{
    [HttpPost("/api/cash-shifts/{cashShiftId:guid}/movements")]
    [ProducesResponseType(typeof(ResponseDTO<CashMovementDTO>), 201)]
    [ProducesResponseType(typeof(ResponseDTO<CashMovementDTO>), 200)]
    public Task<IActionResult> Create(Guid cashShiftId, CreateCashMovementDTO body,
        [FromHeader(Name = "Idempotency-Key")] string key, CancellationToken ct)
    {
        return ExecuteWithStatus(async options =>
        {
            var request = new CreateCashMovementRequest
            {
                CashShiftId = cashShiftId.ToString(),
                IdempotencyKey = key,
                Type = body.Type ?? string.Empty,
                Reason = body.Reason ?? string.Empty
            };
            if (body.Amount.HasValue)
            {
                request.Amount = body.Amount.Value.ToString(CultureInfo.InvariantCulture);
            }

            var result = await client.CreateAsync(request, options);
            if (result.Created)
            {
                Response.Headers.Location = $"/api/cash-movements/{result.Movement.CashMovementId}";
            }

            return (Map(result.Movement), result.Created ? 201 : 200);
        }, ct);
    }

    [HttpGet("/api/cash-shifts/{cashShiftId:guid}/movements")]
    [ProducesResponseType(typeof(ResponseDTO<CashLedgerDTO>), 200)]
    public Task<IActionResult> GetAll(Guid cashShiftId, CancellationToken ct, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        return Execute(async options =>
        {
            var value = await client.GetAllAsync(new CashLedgerRequest
            {
                CashShiftId = cashShiftId.ToString(),
                Page = page,
                PageSize = pageSize
            }, options);
            return new CashLedgerDTO
            {
                CashShiftId = Guid.Parse(value.CashShiftId),
                RestaurantId = Guid.Parse(value.RestaurantId),
                Currency = value.Currency,
                OpeningCash = decimal.Parse(value.OpeningCash, CultureInfo.InvariantCulture),
                MovementTotal = decimal.Parse(value.MovementTotal, CultureInfo.InvariantCulture),
                ExpectedCash = decimal.Parse(value.ExpectedCash, CultureInfo.InvariantCulture),
                Movements = new PagedResult<CashMovementDTO>(value.Items.Select(Map).ToArray(), value.TotalCount, value.Page, value.PageSize)
            };
        }, ct);
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(ResponseDTO<CashMovementDTO>), 200)]
    public Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        return Execute(async options => Map(await client.GetByIdAsync(new CashMovementIdRequest
        {
            CashMovementId = id.ToString()
        }, options)), ct);
    }

    private static CashMovementDTO Map(CashMovementInfo value)
    {
        return new CashMovementDTO
        {
            CashMovementId = Guid.Parse(value.CashMovementId),
            CashShiftId = Guid.Parse(value.CashShiftId),
            RestaurantId = Guid.Parse(value.RestaurantId),
            PaymentId = value.HasPaymentId ? Guid.Parse(value.PaymentId) : null,
            Type = value.Type,
            SignedAmount = decimal.Parse(value.SignedAmount, CultureInfo.InvariantCulture),
            Reason = value.HasReason ? value.Reason : null,
            PostedAt = DateTimeOffset.Parse(value.PostedAt, CultureInfo.InvariantCulture),
            PerformedByRef = value.HasPerformedByRef ? Guid.Parse(value.PerformedByRef) : null,
            PerformerSource = value.PerformerSource
        };
    }
}
