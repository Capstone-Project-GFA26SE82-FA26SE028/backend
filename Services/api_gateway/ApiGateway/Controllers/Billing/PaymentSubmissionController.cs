using System.Globalization;
using ApiGateway.Settings;
using ApiGateway.Swagger;
using Backend.Contracts.Billing;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace ApiGateway.Controllers.Billing;

[ApiController]
[RequireBearerToken]
[Route("api")]
public sealed class PaymentSubmissionController(PaymentSubmissionGrpc.PaymentSubmissionGrpcClient client,
    IOptions<BillingClientSettings> settings) : BillingControllerBase(settings)
{
    [HttpPost("table-sessions/{sessionId:guid}/payment-requests/{id:guid}/submit")]
    public Task<IActionResult> Submit(Guid sessionId, Guid id, CancellationToken ct)
    {
        return Execute(async options => Map(await client.SubmitAsync(new SubmitPaymentRequest
        {
            TableSessionId = sessionId.ToString(),
            ServiceRequestId = id.ToString()
        }, options)), ct);
    }

    [HttpGet("restaurants/{restaurantId:guid}/payment-requests/incoming")]
    public Task<IActionResult> GetIncoming(Guid restaurantId, CancellationToken ct, int page = 1, int pageSize = 20)
    {
        return Execute(async options =>
        {
            var result = await client.GetIncomingAsync(new PaymentSubmissionListRequest
            {
                RestaurantId = restaurantId.ToString(),
                Page = page,
                PageSize = pageSize
            }, options);
            return new
            {
                Items = result.Items.Select(Map).ToArray(),
                result.TotalCount,
                result.Page,
                result.PageSize
            };
        }, ct);
    }

    private static object Map(PaymentSubmissionInfo value)
    {
        return new
        {
            CheckoutId = value.HasCheckoutId ? Guid.Parse(value.CheckoutId) : (Guid?)null,
            BillId = value.HasBillId ? Guid.Parse(value.BillId) : (Guid?)null,
            TableSessionId = Guid.Parse(value.TableSessionId),
            CashRegisterId = Guid.Parse(value.CashRegisterId),
            SubmittedAt = value.HasSubmittedAt ? DateTimeOffset.Parse(value.SubmittedAt, CultureInfo.InvariantCulture) : (DateTimeOffset?)null,
            value.Status
        };
    }
}
