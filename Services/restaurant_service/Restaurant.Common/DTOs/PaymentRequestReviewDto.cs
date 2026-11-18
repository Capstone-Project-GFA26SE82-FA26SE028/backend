namespace Restaurant.Common.DTOs;

public sealed record PaymentRequestReviewDto(Guid RestaurantId, int PendingOrderCount, IReadOnlyList<PaymentRequestReviewItemDto> Items);

public sealed record PaymentRequestReviewItemDto(Guid OrderItemId, Guid TableId, string Name, int Quantity, string Status);
