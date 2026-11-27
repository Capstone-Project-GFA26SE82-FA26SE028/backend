using System.ComponentModel.DataAnnotations;

namespace ApiGateway.DTOs.Customer;

public sealed class SaveLoyaltyTierRequestDTO
{
    [Required(ErrorMessage = "Mã hạng thành viên là bắt buộc.")]
    [StringLength(50, ErrorMessage = "Mã hạng thành viên tối đa 50 ký tự.")]
    public string LoyaltyTierCode { get; set; } = string.Empty;

    [Required(ErrorMessage = "Tên hạng thành viên là bắt buộc.")]
    [StringLength(100, ErrorMessage = "Tên hạng thành viên tối đa 100 ký tự.")]
    public string LoyaltyTierName { get; set; } = string.Empty;

    [Range(0, double.MaxValue, ErrorMessage = "Mức chi tiêu yêu cầu không được âm.")]
    public decimal RequiredSpend { get; set; }

    [Range(0, 100, ErrorMessage = "Tỷ lệ giảm giá phải từ 0% đến 100%.")]
    public decimal DiscountPercent { get; set; }
}

public sealed class LoyaltyTierResponseDTO
{
    public Guid LoyaltyTierId { get; set; }
    public string LoyaltyTierCode { get; set; } = string.Empty;
    public string LoyaltyTierName { get; set; } = string.Empty;
    public decimal RequiredSpend { get; set; }
    public decimal DiscountPercent { get; set; }
}

public sealed class SaveLoyaltyPolicyRequestDTO
{
    [Range(0.01, double.MaxValue, ErrorMessage = "Số tiền để tích 1 điểm phải lớn hơn 0.")]
    public decimal SpendPerPoint { get; set; }

    [Range(0.01, double.MaxValue, ErrorMessage = "Số tiền quy đổi cho 1 điểm tiêu phải lớn hơn 0.")]
    public decimal MoneyPerRedeemedPoint { get; set; }
}

public sealed class LoyaltyPolicyResponseDTO
{
    public Guid PolicyId { get; set; }
    public Guid RestaurantId { get; set; }
    public int Version { get; set; }
    public decimal SpendPerPoint { get; set; }
    public decimal MoneyPerRedeemedPoint { get; set; }
    public DateTimeOffset EffectiveFrom { get; set; }
}
