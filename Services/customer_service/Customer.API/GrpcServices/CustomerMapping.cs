using Backend.Contracts.Customer;
using Customer.Common.DTOs;

namespace Customer.API.GrpcServices;

public static class CustomerMapping
{
    public static CustomerInfo Map(CustomerDTO dto)
    {
        return new CustomerInfo
        {
            CustomerId = dto.CustomerId.ToString(),
            NormalizedPhone = dto.NormalizedPhone,
            CustomerName = dto.CustomerName,
            LoyaltyTierId = dto.LoyaltyTierId.ToString(),
            LoyaltyTierName = dto.LoyaltyTierName,
            LoyaltyTierCode = dto.LoyaltyTierCode,
            DiscountPercent = dto.DiscountPercent.ToString("0.##"),
            AvailablePoints = dto.AvailablePoints,
            TotalSpent = dto.TotalSpent.ToString("0.##")
        };
    }

    public static LoyaltyTierInfo Map(LoyaltyTierDTO dto)
    {
        return new LoyaltyTierInfo
        {
            LoyaltyTierId = dto.LoyaltyTierId.ToString(),
            LoyaltyTierCode = dto.LoyaltyTierCode,
            LoyaltyTierName = dto.LoyaltyTierName,
            RequiredSpend = dto.RequiredSpend.ToString("0.##"),
            DiscountPercent = dto.DiscountPercent.ToString("0.##")
        };
    }

    public static FeedbackInfo Map(FeedbackDTO dto)
    {
        var reply = new FeedbackInfo
        {
            FeedbackId = dto.FeedbackId.ToString(),
            OrderRef = dto.OrderRef.ToString(),
            CustomerId = dto.CustomerId?.ToString(),
            CustomerName = dto.CustomerName,
            Star = dto.Star,
            Comment = dto.Comment,
            SubmittedAt = dto.SubmittedAt.ToString("o")
        };

        if (dto.Items is not null)
        {
            reply.Items.AddRange(dto.Items.Select(i => new FeedbackItemInfo
            {
                OrderItemRef = i.OrderItemRef.ToString(),
                Star = i.Star,
                Comment = i.Comment
            }));
        }

        return reply;
    }
}

