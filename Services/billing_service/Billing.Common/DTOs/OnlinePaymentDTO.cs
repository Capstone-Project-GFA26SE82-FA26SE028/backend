namespace Billing.Common.DTOs;

public sealed record OnlinePaymentCommand(Guid OrderId, Guid RestaurantId, Guid CustomerUserId,
    decimal Amount, string Action, string Reason = "", string Reference = "", string Evidence = "");

public sealed record OnlinePaymentDTO(string Status, string? CheckoutUrl, decimal Amount,
    DateTime? PaidAt, DateTime? RefundedAt, string? RefundReference);
