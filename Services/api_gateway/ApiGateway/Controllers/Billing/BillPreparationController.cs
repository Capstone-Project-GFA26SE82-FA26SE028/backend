using System.Globalization;
using ApiGateway.Settings;
using ApiGateway.Swagger;
using Backend.Contracts.Billing;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace ApiGateway.Controllers.Billing;

[ApiController]
[RequireBearerToken]
[Route("api/table-sessions/{sessionId:guid}/payment-requests/{id:guid}")]
public sealed class BillPreparationController(BillCheckoutGrpc.BillCheckoutGrpcClient client,
    IOptions<BillingClientSettings> settings) : BillingControllerBase(settings)
{
    [HttpPost("prepare")]
    public Task<IActionResult> Prepare(Guid sessionId, Guid id, CancellationToken ct)
    {
        return Execute(async options => Map(await client.PrepareAsync(RequestFor(sessionId, id), options)), ct);
    }

    [HttpPost("/api/table-sessions/{sessionId:guid}/checkout/prepare-at-counter")]
    public Task<IActionResult> PrepareAtCounter(Guid sessionId, [FromHeader(Name = "Idempotency-Key")] Guid checkoutId, CancellationToken ct)
    {
        return Execute(async options => Map(await client.PrepareAtCounterAsync(RequestFor(sessionId, checkoutId), options)), ct);
    }

    [HttpPost("/api/table-sessions/{sessionId:guid}/checkout/{id:guid}/present-at-counter")]
    public Task<IActionResult> PresentAtCounter(Guid sessionId, Guid id, CancellationToken ct)
    {
        return Execute(async options => Map(await client.PresentAtCounterAsync(RequestFor(sessionId, id), options)), ct);
    }

    [HttpGet("bill")]
    public Task<IActionResult> GetPrepared(Guid sessionId, Guid id, CancellationToken ct)
    {
        return Execute(async options => Map(await client.GetPreparedAsync(RequestFor(sessionId, id), options)), ct);
    }

    [HttpPost("present")]
    public Task<IActionResult> Present(Guid sessionId, Guid id, CancellationToken ct)
    {
        return Execute(async options => Map(await client.PresentAsync(RequestFor(sessionId, id), options)), ct);
    }

    private static BillCheckoutRequest RequestFor(Guid sessionId, Guid id)
    {
        return new BillCheckoutRequest
        {
            TableSessionId = sessionId.ToString(),
            CheckoutId = id.ToString()
        };
    }

    private static object Map(PreparedBillInfo value)
    {
        return new
        {
            CheckoutId = Guid.Parse(value.CheckoutId),
            TableSessionId = Guid.Parse(value.TableSessionId),
            value.CheckoutStatus,
            Bill = BillController.Map(value.Bill),
            PreparedByRef = Guid.Parse(value.PreparedByRef),
            PreparedAt = DateTimeOffset.Parse(value.PreparedAt, CultureInfo.InvariantCulture),
            PresentedAt = value.HasPresentedAt
                ? DateTimeOffset.Parse(value.PresentedAt, CultureInfo.InvariantCulture) : (DateTimeOffset?)null
        };
    }
}
