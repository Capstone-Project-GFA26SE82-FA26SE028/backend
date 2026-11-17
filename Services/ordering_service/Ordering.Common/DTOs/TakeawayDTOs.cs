namespace Ordering.Common.DTOs;

public sealed class SubmitTakeawayDTO
{
    public Guid CartId { get; set; }
    public string CartVersion { get; set; } = "";
    public string PickupName { get; set; } = "";
    public string PickupPhone { get; set; } = "";
}

public sealed record TakeawayPaymentResult(string Status, string? CheckoutUrl, DateTimeOffset? PaidAt,
    DateTimeOffset? RefundedAt, string? RefundReference);
