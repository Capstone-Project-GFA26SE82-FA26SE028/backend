using System.Globalization;
using System.Text.Json;
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
[Route("api/bills")]
[RequireBearerToken]
public sealed class BillController(BillGrpc.BillGrpcClient client, IOptions<BillingClientSettings> settings)
    : BillingControllerBase(settings)
{
    [HttpPost]
    [ProducesResponseType(typeof(ResponseDTO<BillDTO>), StatusCodes.Status201Created)]
    public Task<IActionResult> Create(CreateBillDTO body, CancellationToken ct)
    {
        return Execute(async options => Map(await client.CreateAsync(new CreateBillRequest
        {
            TableSessionId = body.TableSessionId.ToString()
        }, options)), ct, 201);
    }

    [HttpGet]
    [ProducesResponseType(typeof(ResponseDTO<PagedResult<BillDTO>>), StatusCodes.Status200OK)]
    public Task<IActionResult> GetAll([FromQuery] BillQueryDTO query, CancellationToken ct)
    {
        return Execute(async options =>
        {
            var request = new BillListRequest
            {
                Page = query.Page,
                PageSize = query.PageSize
            };
            if (query.RestaurantId.HasValue)
            {
                request.RestaurantId = query.RestaurantId.Value.ToString();
            }

            if (query.Search is not null)
            {
                request.Search = query.Search;
            }

            var result = await client.GetAllAsync(request, options);
            return new PagedResult<BillDTO>(result.Items.Select(Map).ToArray(), result.TotalCount, result.Page, result.PageSize);
        }, ct);
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(ResponseDTO<BillDTO>), StatusCodes.Status200OK)]
    public Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        return Execute(async options => Map(await client.GetByIdAsync(new BillIdRequest
        {
            BillId = id.ToString()
        }, options)), ct);
    }

    private static decimal Decimal(string value)
    {
        return decimal.Parse(value, CultureInfo.InvariantCulture);
    }

    private static BillDTO Map(BillInfo source)
    {
        return new BillDTO
        {
            BillId = Guid.Parse(source.BillId),
            RestaurantId = Guid.Parse(source.RestaurantId),
            BillNumber = source.BillNumber,
            Status = source.Status,
            Currency = source.Currency,
            SubTotal = Decimal(source.SubTotal),
            DiscountAmount = Decimal(source.DiscountAmount),
            VatRateSnapshot = Decimal(source.VatRateSnapshot),
            ServiceRateSnapshot = Decimal(source.ServiceRateSnapshot),
            VatAmount = Decimal(source.VatAmount),
            ServiceAmount = Decimal(source.ServiceAmount),
            TotalAmount = Decimal(source.TotalAmount),
            RoundingAdjustment = Decimal(source.RoundingAdjustment),
            Version = source.Version,
            HasCompleteCogs = source.HasCompleteCogs,
            SourceStatus = source.SourceStatus,
            Warnings = source.Warnings.ToList(),
            Items = source.Items.Select(line => new BillLineDTO
            {
                BillLineId = Guid.Parse(line.BillLineId),
                OrderItemRef = Guid.Parse(line.OrderItemRef),
                TableSessionRef = Guid.Parse(line.TableSessionRef),
                OriginalTableRef = Guid.Parse(line.OriginalTableRef),
                ComboGroupRef = line.HasComboGroupRef ? Guid.Parse(line.ComboGroupRef) : null,
                Item = JsonSerializer.Deserialize<BillItemSnapshot>(line.ItemSnapshotJson) ?? new BillItemSnapshot(),
                Quantity = Decimal(line.Quantity),
                UnitPriceSnapshot = Decimal(line.UnitPriceSnapshot),
                CogsSnapshot = line.HasCogsSnapshot ? Decimal(line.CogsSnapshot) : null,
                LineTotal = Decimal(line.LineTotal)
            }).ToList()
        };
    }
}
