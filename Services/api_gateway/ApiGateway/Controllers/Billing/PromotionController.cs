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
[Route("api/promotions")]
[RequireBearerToken]
public sealed class PromotionController(PromotionGrpc.PromotionGrpcClient client, IOptions<BillingClientSettings> settings)
    : BillingControllerBase(settings)
{
    [HttpPost]
    [ProducesResponseType(typeof(ResponseDTO<PromotionDTO>), StatusCodes.Status201Created)]
    public Task<IActionResult> Create(CreatePromotionDTO body, CancellationToken ct)
    {
        return Execute(async options =>
        {
            var request = new CreatePromotionRequest
            {
                PromotionName = body.PromotionName,
                DiscountType = body.DiscountType,
                Type = body.Type,
                DiscountValue = body.DiscountValue.ToString(CultureInfo.InvariantCulture),
                StartAt = body.StartAt.ToString("o"),
                IsActive = body.IsActive
            };

            if (body.UsageLimit.HasValue)
            {
                request.UsageLimit = body.UsageLimit.Value;
            }

            if (body.MinimumSpend.HasValue)
            {
                request.MinimumSpend = body.MinimumSpend.Value.ToString(CultureInfo.InvariantCulture);
            }

            if (body.EndAt.HasValue)
            {
                request.EndAt = body.EndAt.Value.ToString("o");
            }

            foreach (var target in body.Targets)
            {
                var item = new CreatePromotionTargetInfo
                {
                    Role = target.Role,
                    RequiredQuantity = target.RequiredQuantity.ToString(CultureInfo.InvariantCulture)
                };

                if (target.ItemRef.HasValue)
                {
                    item.ItemRef = target.ItemRef.Value.ToString();
                }

                if (target.CategoryRef.HasValue)
                {
                    item.CategoryRef = target.CategoryRef.Value.ToString();
                }

                request.Targets.Add(item);
            }

            return Map(await client.CreateAsync(request, options));
        }, ct, 201);
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(ResponseDTO<PromotionDTO>), StatusCodes.Status200OK)]
    public Task<IActionResult> Update(Guid id, UpdatePromotionDTO body, CancellationToken ct)
    {
        return Execute(async options =>
        {
            var request = new UpdatePromotionRequest
            {
                PromotionId = id.ToString()
            };

            if (!string.IsNullOrWhiteSpace(body.PromotionName))
            {
                request.PromotionName = body.PromotionName;
            }

            if (!string.IsNullOrWhiteSpace(body.DiscountType))
            {
                request.DiscountType = body.DiscountType;
            }

            if (!string.IsNullOrWhiteSpace(body.Type))
            {
                request.Type = body.Type;
            }

            if (body.DiscountValue.HasValue)
            {
                request.DiscountValue = body.DiscountValue.Value.ToString(CultureInfo.InvariantCulture);
            }

            if (body.UsageLimit.HasValue)
            {
                request.UsageLimit = body.UsageLimit.Value;
            }

            if (body.MinimumSpend.HasValue)
            {
                request.MinimumSpend = body.MinimumSpend.Value.ToString(CultureInfo.InvariantCulture);
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
    [ProducesResponseType(typeof(ResponseDTO<PagedResult<PromotionDTO>>), StatusCodes.Status200OK)]
    public Task<IActionResult> GetAll([FromQuery] PromotionQueryDTO query, CancellationToken ct)
    {
        return Execute(async options =>
        {
            var request = new PromotionListRequest
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
            return new PagedResult<PromotionDTO>(
                result.Items.Select(Map).ToArray(),
                result.TotalCount,
                result.Page,
                result.PageSize);
        }, ct);
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(ResponseDTO<PromotionDTO>), StatusCodes.Status200OK)]
    public Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        return Execute(async options => Map(await client.GetByIdAsync(new PromotionIdRequest
        {
            PromotionId = id.ToString()
        }, options)), ct);
    }

    [HttpPatch("{id:guid}/status")]
    [ProducesResponseType(typeof(ResponseDTO<PromotionDTO>), StatusCodes.Status200OK)]
    public Task<IActionResult> SetStatus(Guid id, [FromBody] SetStatusDTO body, CancellationToken ct)
    {
        return Execute(async options => Map(await client.SetStatusAsync(new SetPromotionStatusRequest
        {
            PromotionId = id.ToString(),
            IsActive = body.IsActive
        }, options)), ct);
    }

    private static decimal Decimal(string value)
    {
        return decimal.Parse(value, CultureInfo.InvariantCulture);
    }

    internal static PromotionDTO Map(PromotionInfo source)
    {
        return new PromotionDTO
        {
            PromotionId = Guid.Parse(source.PromotionId),
            PromotionName = source.PromotionName,
            DiscountType = source.DiscountType,
            Type = source.Type,
            DiscountValue = Decimal(source.DiscountValue),
            UsageLimit = source.HasUsageLimit ? source.UsageLimit : null,
            CurrentUsage = source.CurrentUsage,
            MinimumSpend = source.HasMinimumSpend ? Decimal(source.MinimumSpend) : null,
            StartAt = DateTime.Parse(source.StartAt, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal),
            EndAt = source.HasEndAt ? DateTime.Parse(source.EndAt, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal) : null,
            IsActive = source.IsActive,
            Targets = source.Targets.Select(t => new PromotionTargetDTO
            {
                PromotionTargetId = Guid.Parse(t.PromotionTargetId),
                PromotionId = Guid.Parse(t.PromotionId),
                ItemRef = t.HasItemRef ? Guid.Parse(t.ItemRef) : null,
                CategoryRef = t.HasCategoryRef ? Guid.Parse(t.CategoryRef) : null,
                Role = t.Role,
                RequiredQuantity = Decimal(t.RequiredQuantity)
            }).ToList()
        };
    }
}
