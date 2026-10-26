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
[Route("api/cash-registers/{cashRegisterId:guid}/shifts")]
[RequireBearerToken]
public sealed class CashShiftController(CashShiftGrpc.CashShiftGrpcClient client,
    IOptions<BillingClientSettings> settings) : BillingControllerBase(settings)
{
    [HttpPost("open")]
    [ProducesResponseType(typeof(ResponseDTO<CashShiftDTO>), StatusCodes.Status201Created)]
    public Task<IActionResult> Open(Guid cashRegisterId, OpenCashShiftDTO body, CancellationToken ct)
    {
        return Execute(async options =>
        {
            var request = new OpenCashShiftRequest
            {
                CashRegisterId = cashRegisterId.ToString()
            };
            if (body.OpeningCash.HasValue)
            {
                request.OpeningCash = body.OpeningCash.Value.ToString(CultureInfo.InvariantCulture);
            }

            return Map(await client.OpenAsync(request, options));
        }, ct, 201);
    }

    [HttpGet("current")]
    [ProducesResponseType(typeof(ResponseDTO<CashShiftDTO>), StatusCodes.Status200OK)]
    public Task<IActionResult> GetCurrent(Guid cashRegisterId, CancellationToken ct)
    {
        return Execute(async options => Map(await client.GetCurrentAsync(new CashShiftRegisterRequest
        {
            CashRegisterId = cashRegisterId.ToString()
        }, options)), ct);
    }

    private static CashShiftDTO Map(CashShiftInfo value)
    {
        return new CashShiftDTO
        {
            CashShiftId = Guid.Parse(value.CashShiftId),
            CashRegisterId = Guid.Parse(value.CashRegisterId),
            RestaurantId = Guid.Parse(value.RestaurantId),
            CashierUserRef = Guid.Parse(value.CashierUserRef),
            OpeningCash = decimal.Parse(value.OpeningCash, CultureInfo.InvariantCulture),
            OpenedAt = DateTimeOffset.Parse(value.OpenedAt, CultureInfo.InvariantCulture),
            Status = value.Status
        };
    }
}
