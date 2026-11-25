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
[Route("api/vouchers")]
[RequireBearerToken]
public sealed class VoucherController(VoucherGrpc.VoucherGrpcClient client, IOptions<BillingClientSettings> settings)
    : BillingControllerBase(settings)
{
    [HttpPost]
    [ProducesResponseType(typeof(ResponseDTO<VoucherDTO>), StatusCodes.Status201Created)]
    public Task<IActionResult> Create(CreateVoucherDTO body, CancellationToken ct)
    {
        return Execute(async options =>
        {
            var request = new CreateVoucherRequest
            {
                VoucherCode = body.VoucherCode,
                DiscountType = body.DiscountType,
                DiscountValue = body.DiscountValue.ToString(CultureInfo.InvariantCulture),
                StartAt = body.StartAt.ToString("o"),
                IsActive = body.IsActive
            };

            if (body.MaximumDiscount.HasValue)
            {
                request.MaximumDiscount = body.MaximumDiscount.Value.ToString(CultureInfo.InvariantCulture);
            }

            if (body.UsageLimit.HasValue)
            {
                request.UsageLimit = body.UsageLimit.Value;
            }

            if (body.EndAt.HasValue)
            {
                request.EndAt = body.EndAt.Value.ToString("o");
            }

            return Map(await client.CreateAsync(request, options));
        }, ct, 201);
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(ResponseDTO<VoucherDTO>), StatusCodes.Status200OK)]
    public Task<IActionResult> Update(Guid id, UpdateVoucherDTO body, CancellationToken ct)
    {
        return Execute(async options =>
        {
            var request = new UpdateVoucherRequest
            {
                VoucherId = id.ToString()
            };

            if (!string.IsNullOrWhiteSpace(body.DiscountType))
            {
                request.DiscountType = body.DiscountType;
            }

            if (body.DiscountValue.HasValue)
            {
                request.DiscountValue = body.DiscountValue.Value.ToString(CultureInfo.InvariantCulture);
            }

            if (body.MaximumDiscount.HasValue)
            {
                request.MaximumDiscount = body.MaximumDiscount.Value.ToString(CultureInfo.InvariantCulture);
            }

            if (body.UsageLimit.HasValue)
            {
                request.UsageLimit = body.UsageLimit.Value;
            }

            if (body.StartAt.HasValue)
            {
                request.StartAt = body.StartAt.Value.ToString("o");
            }

            if (body.EndAt.HasValue)
            {
                request.EndAt = body.EndAt.Value.ToString("o");
            }

            if (body.IsActive.HasValue)
            {
                request.IsActive = body.IsActive.Value;
            }

            return Map(await client.UpdateAsync(request, options));
        }, ct);
    }

    [HttpGet]
    [ProducesResponseType(typeof(ResponseDTO<PagedResult<VoucherDTO>>), StatusCodes.Status200OK)]
    public Task<IActionResult> GetAll([FromQuery] VoucherQueryDTO query, CancellationToken ct)
    {
        return Execute(async options =>
        {
            var request = new VoucherListRequest
            {
                Page = query.Page,
                PageSize = query.PageSize
            };

            if (!string.IsNullOrWhiteSpace(query.Search))
            {
                request.Search = query.Search;
            }

            if (query.IsActive.HasValue)
            {
                request.IsActive = query.IsActive.Value;
            }

            var result = await client.GetAllAsync(request, options);
            return new PagedResult<VoucherDTO>(
                result.Items.Select(Map).ToArray(),
                result.TotalCount,
                result.Page,
                result.PageSize);
        }, ct);
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(ResponseDTO<VoucherDTO>), StatusCodes.Status200OK)]
    public Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        return Execute(async options => Map(await client.GetByIdAsync(new VoucherIdRequest
        {
            VoucherId = id.ToString()
        }, options)), ct);
    }

    [HttpPatch("{id:guid}/status")]
    [ProducesResponseType(typeof(ResponseDTO<VoucherDTO>), StatusCodes.Status200OK)]
    public Task<IActionResult> SetStatus(Guid id, [FromBody] SetStatusDTO body, CancellationToken ct)
    {
        return Execute(async options => Map(await client.SetStatusAsync(new SetVoucherStatusRequest
        {
            VoucherId = id.ToString(),
            IsActive = body.IsActive
        }, options)), ct);
    }

    private static decimal Decimal(string value)
    {
        return decimal.Parse(value, CultureInfo.InvariantCulture);
    }

    internal static VoucherDTO Map(VoucherInfo source)
    {
        return new VoucherDTO
        {
            VoucherId = Guid.Parse(source.VoucherId),
            NormalizedVoucherCode = source.NormalizedVoucherCode,
            DiscountType = source.DiscountType,
            DiscountValue = Decimal(source.DiscountValue),
            MaximumDiscount = source.HasMaximumDiscount ? Decimal(source.MaximumDiscount) : null,
            UsageLimit = source.HasUsageLimit ? source.UsageLimit : null,
            CurrentUsage = source.CurrentUsage,
            StartAt = DateTime.Parse(source.StartAt, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal),
            EndAt = source.HasEndAt ? DateTime.Parse(source.EndAt, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal) : null,
            IsActive = source.IsActive
        };
    }
}
