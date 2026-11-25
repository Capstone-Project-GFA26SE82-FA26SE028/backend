using System.Globalization;
using Backend.Contracts.Billing;
using Billing.BLL.Interfaces;
using Billing.Common.DTOs;
using Grpc.Core;

namespace Billing.API.GrpcServices;

public sealed class PromotionGrpcService(IPromotionService service) : PromotionGrpc.PromotionGrpcBase
{
    public override async Task<PromotionInfo> Create(CreatePromotionRequest request, ServerCallContext context)
    {
        var dto = new CreatePromotionDTO
        {
            PromotionName = request.PromotionName,
            DiscountType = request.DiscountType,
            Type = request.Type,
            DiscountValue = ParseDecimal(request.DiscountValue),
            UsageLimit = request.HasUsageLimit ? request.UsageLimit : null,
            MinimumSpend = request.HasMinimumSpend ? ParseDecimal(request.MinimumSpend) : null,
            StartAt = DateTime.Parse(request.StartAt, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal),
            EndAt = request.HasEndAt ? DateTime.Parse(request.EndAt, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal) : null,
            IsActive = request.IsActive,
            Targets = request.Targets.Select(t => new CreatePromotionTargetDTO
            {
                ItemRef = string.IsNullOrWhiteSpace(t.ItemRef) ? null : Guid.Parse(t.ItemRef),
                CategoryRef = string.IsNullOrWhiteSpace(t.CategoryRef) ? null : Guid.Parse(t.CategoryRef),
                Role = t.Role,
                RequiredQuantity = ParseDecimal(t.RequiredQuantity)
            }).ToList()
        };

        var result = await service.Create(dto, context.CancellationToken);
        return Map(result);
    }

    public override async Task<PromotionInfo> Update(UpdatePromotionRequest request, ServerCallContext context)
    {
        var dto = new UpdatePromotionDTO
        {
            PromotionName = request.HasPromotionName ? request.PromotionName : null,
            DiscountType = request.HasDiscountType ? request.DiscountType : null,
            Type = request.HasType ? request.Type : null,
            DiscountValue = request.HasDiscountValue ? ParseDecimal(request.DiscountValue) : null,
            UsageLimit = request.HasUsageLimit ? request.UsageLimit : null,
            MinimumSpend = request.HasMinimumSpend ? ParseDecimal(request.MinimumSpend) : null,
            StartAt = request.HasStartAt ? DateTime.Parse(request.StartAt, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal) : null,
            EndAt = request.HasEndAt ? DateTime.Parse(request.EndAt, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal) : null,
            IsActive = request.HasIsActive ? request.IsActive : null
        };

        var result = await service.Update(Guid.Parse(request.PromotionId), dto, context.CancellationToken);
        return Map(result);
    }

    public override async Task<PromotionPage> GetAll(PromotionListRequest request, ServerCallContext context)
    {
        var result = await service.GetAll(new PromotionQueryDTO
        {
            Search = request.HasSearch ? request.Search : null,
            IsActive = request.HasIsActive ? request.IsActive : null,
            Page = request.Page,
            PageSize = request.PageSize
        }, context.CancellationToken);

        var page = new PromotionPage
        {
            TotalCount = result.TotalCount,
            Page = result.Page,
            PageSize = result.PageSize
        };
        page.Items.AddRange(result.Items.Select(Map));
        return page;
    }

    public override async Task<PromotionInfo> GetById(PromotionIdRequest request, ServerCallContext context)
    {
        var result = await service.GetById(Guid.Parse(request.PromotionId), context.CancellationToken);
        return Map(result);
    }

    public override async Task<PromotionInfo> SetStatus(SetPromotionStatusRequest request, ServerCallContext context)
    {
        var result = await service.SetStatus(Guid.Parse(request.PromotionId), request.IsActive, context.CancellationToken);
        return Map(result);
    }

    private static decimal ParseDecimal(string value)
    {
        return decimal.Parse(value, CultureInfo.InvariantCulture);
    }

    private static string Decimal(decimal value)
    {
        return value.ToString(CultureInfo.InvariantCulture);
    }

    private static PromotionInfo Map(PromotionDTO dto)
    {
        var info = new PromotionInfo
        {
            PromotionId = dto.PromotionId.ToString(),
            PromotionName = dto.PromotionName,
            DiscountType = dto.DiscountType,
            Type = dto.Type,
            DiscountValue = Decimal(dto.DiscountValue),
            CurrentUsage = dto.CurrentUsage,
            StartAt = dto.StartAt.ToString("o"),
            IsActive = dto.IsActive
        };

        if (dto.UsageLimit.HasValue)
        {
            info.UsageLimit = dto.UsageLimit.Value;
        }

        if (dto.MinimumSpend.HasValue)
        {
            info.MinimumSpend = Decimal(dto.MinimumSpend.Value);
        }

        if (dto.EndAt.HasValue)
        {
            info.EndAt = dto.EndAt.Value.ToString("o");
        }

        info.Targets.AddRange(dto.Targets.Select(t => new PromotionTargetInfo
        {
            PromotionTargetId = t.PromotionTargetId.ToString(),
            PromotionId = t.PromotionId.ToString(),
            ItemRef = t.ItemRef?.ToString(),
            CategoryRef = t.CategoryRef?.ToString(),
            Role = t.Role,
            RequiredQuantity = Decimal(t.RequiredQuantity)
        }));

        return info;
    }
}
