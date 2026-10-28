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
[Route("api/shift-reports")]
[RequireBearerToken]
public sealed class ShiftReportController(ShiftReportGrpc.ShiftReportGrpcClient client,
    IOptions<BillingClientSettings> settings) : BillingControllerBase(settings)
{
    [HttpGet("/api/cash-shifts/{cashShiftId:guid}/reconciliation")]
    [ProducesResponseType(typeof(ResponseDTO<ShiftReconciliationDTO>), 200)]
    public Task<IActionResult> Preview(Guid cashShiftId, CancellationToken ct)
    {
        return Execute(async options =>
        {
            var value = await client.PreviewAsync(new ShiftReportShiftRequest
            {
                CashShiftId = cashShiftId.ToString()
            }, options);
            return new ShiftReconciliationDTO
            {
                CashShiftId = Guid.Parse(value.CashShiftId),
                RestaurantId = Guid.Parse(value.RestaurantId),
                Status = value.Status,
                OpeningCash = Money(value.OpeningCash),
                MovementTotal = Money(value.MovementTotal),
                ExpectedCash = Money(value.ExpectedCash),
                SaleTotal = Money(value.SaleTotal),
                CashInTotal = Money(value.CashInTotal),
                CashOutTotal = Money(value.CashOutTotal),
                PaymentCount = value.PaymentCount,
                CanClose = value.CanClose,
                Blockers = value.Blockers.Select(item => new ReconciliationBlockerDTO
                {
                    Code = item.Code,
                    Message = item.Message,
                    PaymentId = item.HasPaymentId ? Guid.Parse(item.PaymentId) : null,
                    CashMovementId = item.HasCashMovementId ? Guid.Parse(item.CashMovementId) : null
                }).ToList()
            };
        }, ct);
    }

    [HttpPost("/api/cash-shifts/{cashShiftId:guid}/close")]
    [ProducesResponseType(typeof(ResponseDTO<ShiftReportDTO>), 201)]
    [ProducesResponseType(typeof(ResponseDTO<ShiftReportDTO>), 200)]
    public Task<IActionResult> Close(Guid cashShiftId, CloseCashShiftDTO body, CancellationToken ct)
    {
        return ExecuteWithStatus(async options =>
        {
            var request = new CloseCashShiftRequest
            {
                CashShiftId = cashShiftId.ToString(),
                DifferenceReason = body.DifferenceReason ?? string.Empty
            };
            if (body.ActualCash.HasValue)
            {
                request.ActualCash = body.ActualCash.Value.ToString(CultureInfo.InvariantCulture);
            }

            if (body.CashHandleOver.HasValue)
            {
                request.CashHandleOver = body.CashHandleOver.Value.ToString(CultureInfo.InvariantCulture);
            }

            var result = await client.CloseAsync(request, options);
            if (result.Created)
            {
                Response.Headers.Location = $"/api/shift-reports/{result.Report.ShiftReportId}";
            }

            return (Map(result.Report), result.Created ? 201 : 200);
        }, ct);
    }

    [HttpGet("/api/cash-shifts/{cashShiftId:guid}/report")]
    [ProducesResponseType(typeof(ResponseDTO<ShiftReportDTO>), 200)]
    public Task<IActionResult> GetForShift(Guid cashShiftId, CancellationToken ct)
    {
        return Execute(async options => Map(await client.GetForShiftAsync(new ShiftReportShiftRequest
        {
            CashShiftId = cashShiftId.ToString()
        }, options)), ct);
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(ResponseDTO<ShiftReportDTO>), 200)]
    public Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        return Execute(async options => Map(await client.GetByIdAsync(new ShiftReportIdRequest
        {
            ShiftReportId = id.ToString()
        }, options)), ct);
    }

    [HttpGet]
    [ProducesResponseType(typeof(ResponseDTO<PagedResult<ShiftReportDTO>>), 200)]
    public Task<IActionResult> GetAll(CancellationToken ct, [FromQuery] Guid? restaurantId = null,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        return Execute(async options =>
        {
            var request = new ShiftReportListRequest
            {
                Page = page,
                PageSize = pageSize
            };
            if (restaurantId.HasValue)
            {
                request.RestaurantId = restaurantId.Value.ToString();
            }

            var result = await client.GetAllAsync(request, options);
            return new PagedResult<ShiftReportDTO>(result.Items.Select(Map).ToArray(), result.TotalCount, result.Page, result.PageSize);
        }, ct);
    }

    private static decimal Money(string value)
    {
        return decimal.Parse(value, CultureInfo.InvariantCulture);
    }

    private static ShiftReportDTO Map(ShiftReportInfo value)
    {
        return new ShiftReportDTO
        {
            ShiftReportId = Guid.Parse(value.ShiftReportId),
            CashShiftId = Guid.Parse(value.CashShiftId),
            CashRegisterId = Guid.Parse(value.CashRegisterId),
            RestaurantId = Guid.Parse(value.RestaurantId),
            CashierUserRef = Guid.Parse(value.CashierUserRef),
            PerformerSource = value.PerformerSource,
            ExpectedCash = Money(value.ExpectedCash),
            ActualCash = Money(value.ActualCash),
            CashHandleOver = Money(value.CashHandleOver),
            Difference = Money(value.Difference),
            RemainingCash = Money(value.RemainingCash),
            DifferenceReason = value.HasDifferenceReason ? value.DifferenceReason : null,
            ClosedAt = DateTimeOffset.Parse(value.ClosedAt, CultureInfo.InvariantCulture)
        };
    }
}
