using ApiGateway.Controllers.Ordering;
using ApiGateway.Settings;
using ApiGateway.Swagger;
using Backend.Contracts.Billing;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Shared.Common.DTOs;

namespace ApiGateway.Controllers.Billing;

[ApiController]
[Route("api/table-sessions/{sessionId:guid}/checkout")]
[RequireBearerToken(Optional = true)]
public sealed class BillCheckoutController(BillCheckoutGrpc.BillCheckoutGrpcClient client,
    IOptions<OrderingClientSettings> settings) : OrderingControllerBase(settings)
{
    [HttpPost]
    public Task<IActionResult> RequestCheckout(Guid sessionId, [FromHeader(Name = "Idempotency-Key")] string key, CancellationToken ct)
    {
        return Execute(async () =>
        {
            if (!Guid.TryParse(key, out var id) || id == Guid.Empty)
            {
                return BadRequest(new ResponseDTO("Idempotency-Key phải là UUID hợp lệ không được rỗng. Dùng lại khóa này khi thử lại thanh toán.", 400));
            }

            return Result(Map(await client.RequestAsync(Write(sessionId, id), Options(ct))));
        }, ct);
    }

    [HttpGet]
    public Task<IActionResult> GetCurrent(Guid sessionId, CancellationToken ct)
    {
        return Execute(async () => Result(Map(await client.GetCurrentAsync(Write(sessionId, Guid.Empty), Options(ct)))), ct);
    }

    [HttpPost("{checkoutId:guid}/synchronize")]
    public Task<IActionResult> Synchronize(Guid sessionId, Guid checkoutId, CancellationToken ct)
    {
        return Execute(async () => Result(Map(await client.SynchronizeAsync(Write(sessionId, checkoutId), Options(ct)))), ct);
    }

    [HttpPost("{checkoutId:guid}/cancel")]
    public Task<IActionResult> Cancel(Guid sessionId, Guid checkoutId, CancellationToken ct)
    {
        return Execute(async () => Result(Map(await client.CancelAsync(Write(sessionId, checkoutId), Options(ct)))), ct);
    }

    private BillCheckoutRequest Write(Guid sessionId, Guid id)
    {
        return new BillCheckoutRequest
        {
            TableSessionId = sessionId.ToString(),
            CheckoutId = id.ToString(),
            GuestToken = Access(sessionId).GuestToken
        };
    }

    private static object Map(BillCheckoutInfo reply)
    {
        return new
        {
            CheckoutId = Guid.Parse(reply.CheckoutId),
            TableSessionId = Guid.Parse(reply.TableSessionId),
            BillId = reply.HasBillId ? Guid.Parse(reply.BillId) : (Guid?)null,
            reply.Status,
            SourceHash = reply.HasSourceHash ? reply.SourceHash : null,
            reply.OrderingReopened
        };
    }
}
