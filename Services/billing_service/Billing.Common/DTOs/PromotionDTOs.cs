namespace Billing.Common.DTOs;

public sealed class PromotionDTO
{
    public Guid PromotionId { get; set; }
    public string PromotionName { get; set; } = string.Empty;
    public string DiscountType { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public decimal DiscountValue { get; set; }
    public int? UsageLimit { get; set; }
    public int CurrentUsage { get; set; }
    public decimal? MinimumSpend { get; set; }
    public DateTime StartAt { get; set; }
    public DateTime? EndAt { get; set; }
    public bool IsActive { get; set; }
    public List<PromotionTargetDTO> Targets { get; set; } = [];
}

public sealed class PromotionTargetDTO
{
    public Guid PromotionTargetId { get; set; }
    public Guid PromotionId { get; set; }
    public Guid? ItemRef { get; set; }
    public Guid? CategoryRef { get; set; }
    public string Role { get; set; } = string.Empty;
    public decimal RequiredQuantity { get; set; }
}

public sealed class CreatePromotionDTO
{
    public string PromotionName { get; set; } = string.Empty;
    public string DiscountType { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public decimal DiscountValue { get; set; }
    public int? UsageLimit { get; set; }
    public decimal? MinimumSpend { get; set; }
    public DateTime StartAt { get; set; }
    public DateTime? EndAt { get; set; }
    public bool IsActive { get; set; } = true;
    public List<CreatePromotionTargetDTO> Targets { get; set; } = [];
}

public sealed class CreatePromotionTargetDTO
{
    public Guid? ItemRef { get; set; }
    public Guid? CategoryRef { get; set; }
    public string Role { get; set; } = string.Empty;
    public decimal RequiredQuantity { get; set; }
}

public sealed class UpdatePromotionDTO
{
    public string? PromotionName { get; set; }
    public string? DiscountType { get; set; }
    public string? Type { get; set; }
    public decimal? DiscountValue { get; set; }
    public int? UsageLimit { get; set; }
    public decimal? MinimumSpend { get; set; }
    public DateTime? StartAt { get; set; }
    public DateTime? EndAt { get; set; }
    public bool? IsActive { get; set; }
}

public sealed class PromotionQueryDTO
{
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 10;
    public string? Search { get; set; }
    public bool? IsActive { get; set; }
}

public sealed class ApplicablePromotionDTO
{
    public Guid PromotionId { get; set; }
    public string PromotionName { get; set; } = string.Empty;
    public string DiscountType { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public decimal DiscountValue { get; set; }
    public decimal EstimatedDiscount { get; set; }
    public string Description { get; set; } = string.Empty;
}
