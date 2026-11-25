namespace Billing.Common.DTOs;

public sealed class VoucherDTO
{
    public Guid VoucherId { get; set; }
    public string NormalizedVoucherCode { get; set; } = string.Empty;
    public string DiscountType { get; set; } = string.Empty;
    public decimal DiscountValue { get; set; }
    public decimal? MaximumDiscount { get; set; }
    public int? UsageLimit { get; set; }
    public int CurrentUsage { get; set; }
    public DateTime StartAt { get; set; }
    public DateTime? EndAt { get; set; }
    public bool IsActive { get; set; }
}

public sealed class CreateVoucherDTO
{
    public string VoucherCode { get; set; } = string.Empty;
    public string DiscountType { get; set; } = string.Empty;
    public decimal DiscountValue { get; set; }
    public decimal? MaximumDiscount { get; set; }
    public int? UsageLimit { get; set; }
    public DateTime StartAt { get; set; }
    public DateTime? EndAt { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class UpdateVoucherDTO
{
    public string? DiscountType { get; set; }
    public decimal? DiscountValue { get; set; }
    public decimal? MaximumDiscount { get; set; }
    public int? UsageLimit { get; set; }
    public DateTime? StartAt { get; set; }
    public DateTime? EndAt { get; set; }
    public bool? IsActive { get; set; }
}

public sealed class VoucherQueryDTO
{
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 10;
    public string? Search { get; set; }
    public bool? IsActive { get; set; }
}

public sealed class ApplyVoucherRequestDTO
{
    public string VoucherCode { get; set; } = string.Empty;
}

public sealed class BillAdjustmentDTO
{
    public Guid BillAdjustmentId { get; set; }
    public Guid BillId { get; set; }
    public Guid? VoucherId { get; set; }
    public Guid? PromotionId { get; set; }
    public string Type { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string RuleSnapshot { get; set; } = string.Empty;
    public Guid? ActorUserRef { get; set; }
    public string? Reason { get; set; }
}
