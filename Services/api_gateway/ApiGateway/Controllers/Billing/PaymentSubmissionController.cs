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
[RequireBearerToken]
[Route("api")]
public sealed class PaymentSubmissionController(PaymentSubmissionGrpc.PaymentSubmissionGrpcClient client,
    IOptions<BillingClientSettings> settings) : BillingControllerBase(settings)
{
    [HttpPost("table-sessions/{sessionId:guid}/payment-requests/{id:guid}/submit")]
    public Task<IActionResult> Submit(Guid sessionId, Guid id, CancellationToken ct)
    {
        return Execute(async options => MapSubmission(await client.SubmitAsync(new SubmitPaymentRequest
        {
            TableSessionId = sessionId.ToString(),
            ServiceRequestId = id.ToString()
        }, options)), ct);
    }

    [HttpGet("restaurants/{restaurantId:guid}/payment-requests/incoming")]
    [ProducesResponseType(typeof(ResponseDTO<PaymentSubmissionPageDTO>), StatusCodes.Status200OK)]
    public Task<IActionResult> GetIncoming(Guid restaurantId, [FromQuery] PaymentSubmissionQueryDTO query, CancellationToken ct)
    {
        return GetPage(restaurantId, query, paid: false, ct);
    }

    [HttpGet("restaurants/{restaurantId:guid}/payment-requests/paid")]
    [ProducesResponseType(typeof(ResponseDTO<PaymentSubmissionPageDTO>), StatusCodes.Status200OK)]
    public Task<IActionResult> GetPaid(Guid restaurantId, [FromQuery] PaymentSubmissionQueryDTO query, CancellationToken ct)
    {
        return GetPage(restaurantId, query, paid: true, ct);
    }

    private Task<IActionResult> GetPage(Guid restaurantId, PaymentSubmissionQueryDTO query, bool paid, CancellationToken ct)
    {
        return Execute(async options =>
        {
            var request = new PaymentSubmissionListRequest
            {
                RestaurantId = restaurantId.ToString(),
                Page = query.Page,
                PageSize = query.PageSize
            };
            if (query.Search is not null)
            {
                request.Search = query.Search;
            }

            if (query.AreaId.HasValue)
            {
                request.AreaId = query.AreaId.Value.ToString();
            }

            if (query.CashShiftId.HasValue)
            {
                request.CashShiftId = query.CashShiftId.Value.ToString();
            }

            var result = paid ? await client.GetPaidAsync(request, options) : await client.GetIncomingAsync(request, options);
            return new PaymentSubmissionPageDTO
            {
                Items = result.Items.Select(Map).ToArray(),
                TotalCount = result.TotalCount,
                Page = result.Page,
                PageSize = result.PageSize,
                CashShiftId = Guid.Parse(result.CashShiftId),
                TotalAmount = result.HasTotalAmount ? decimal.Parse(result.TotalAmount, CultureInfo.InvariantCulture) : (decimal?)null,
                KnownTotalAmount = decimal.Parse(result.KnownTotalAmount, CultureInfo.InvariantCulture),
                UnbilledCount = result.UnbilledCount
            };
        }, ct);
    }

    private static PaymentSubmissionDTO Map(PaymentSubmissionInfo value)
    {
        return new PaymentSubmissionDTO
        {
            CheckoutId = value.HasCheckoutId ? Guid.Parse(value.CheckoutId) : (Guid?)null,
            BillId = value.HasBillId ? Guid.Parse(value.BillId) : (Guid?)null,
            TableSessionId = Guid.Parse(value.TableSessionId),
            CashRegisterId = Guid.Parse(value.CashRegisterId),
            SubmittedAt = value.HasSubmittedAt ? DateTimeOffset.Parse(value.SubmittedAt, CultureInfo.InvariantCulture) : (DateTimeOffset?)null,
            Status = value.Status,
            BillCode = value.HasBillCode ? value.BillCode : null,
            TotalAmount = value.HasTotalAmount ? decimal.Parse(value.TotalAmount, CultureInfo.InvariantCulture) : (decimal?)null,
            TableId = value.HasTableId ? Guid.Parse(value.TableId) : (Guid?)null,
            TableCode = value.HasTableCode ? value.TableCode : null,
            AreaId = value.HasAreaId ? Guid.Parse(value.AreaId) : (Guid?)null,
            AreaName = value.HasAreaName ? value.AreaName : null,
            Tables = value.Tables.Select(table => new CashierTableDTO
            {
                TableId = Guid.Parse(table.TableId),
                TableCode = table.TableCode,
                AreaId = Guid.Parse(table.AreaId),
                AreaName = table.AreaName
            }).ToArray(),
            PartySize = value.PartySize,
            OpenedAt = value.HasOpenedAt ? DateTimeOffset.Parse(value.OpenedAt, CultureInfo.InvariantCulture) : (DateTimeOffset?)null,
            PaidAt = value.HasPaidAt ? DateTimeOffset.Parse(value.PaidAt, CultureInfo.InvariantCulture) : (DateTimeOffset?)null
        };
    }

    private static object MapSubmission(PaymentSubmissionInfo value)
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
