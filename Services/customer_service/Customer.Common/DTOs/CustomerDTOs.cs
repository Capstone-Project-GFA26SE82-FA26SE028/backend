namespace Customer.Common.DTOs;

public sealed class CustomerDTO
{
    public Guid CustomerId { get; set; }
    public string NormalizedPhone { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public Guid LoyaltyTierId { get; set; }
    public string LoyaltyTierName { get; set; } = string.Empty;
    public string LoyaltyTierCode { get; set; } = string.Empty;
    public decimal DiscountPercent { get; set; }
    public int AvailablePoints { get; set; }
    public decimal TotalSpent { get; set; }
}

public sealed class CreateCustomerDTO
{
    public string PhoneNumber { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
}

public sealed class LoyaltyTierDTO
{
    public Guid LoyaltyTierId { get; set; }
    public string LoyaltyTierCode { get; set; } = string.Empty;
    public string LoyaltyTierName { get; set; } = string.Empty;
    public decimal RequiredSpend { get; set; }
    public decimal DiscountPercent { get; set; }
}

public sealed class LoyaltyEarnRequestDTO
{
    public Guid CustomerId { get; set; }
    public Guid BillId { get; set; }
    public decimal FinalAmount { get; set; }
}

public sealed class LoyaltyEarnResultDTO
{
    public int PointsEarned { get; set; }
    public int NewTotalPoints { get; set; }
    public string LoyaltyTierName { get; set; } = string.Empty;
    public bool TierUpgraded { get; set; }
}

public sealed class PointReservationRequestDTO
{
    public Guid CustomerId { get; set; }
    public Guid BillId { get; set; }
    public int Points { get; set; }
}

public sealed class PointReservationResultDTO
{
    public Guid PointReservationId { get; set; }
    public int ReservedPoints { get; set; }
    public decimal DiscountAmount { get; set; }
    public int RemainingPoints { get; set; }
}

public sealed class FeedbackItemDTO
{
    public Guid OrderItemRef { get; set; }
    public int Star { get; set; }
    public string? Comment { get; set; }
}

public sealed class SubmitFeedbackDTO
{
    public Guid OrderRef { get; set; }
    public string? AccessToken { get; set; }
    public Guid? CustomerId { get; set; }
    public int Star { get; set; }
    public string? Comment { get; set; }
    public List<FeedbackItemDTO> Items { get; set; } = new List<FeedbackItemDTO>();
}

public sealed class FeedbackDTO
{
    public Guid FeedbackId { get; set; }
    public Guid OrderRef { get; set; }
    public Guid? CustomerId { get; set; }
    public string? CustomerName { get; set; }
    public int Star { get; set; }
    public string? Comment { get; set; }
    public DateTime SubmittedAt { get; set; }
    public List<FeedbackItemDTO> Items { get; set; } = new List<FeedbackItemDTO>();
}

public sealed class FeedbackSummaryDTO
{
    public double AverageRating { get; set; }
    public int TotalFeedbacks { get; set; }
    public int Star5Count { get; set; }
    public int Star4Count { get; set; }
    public int Star3Count { get; set; }
    public int Star2Count { get; set; }
    public int Star1Count { get; set; }
}

