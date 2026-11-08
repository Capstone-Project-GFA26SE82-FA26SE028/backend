namespace ApiGateway.DTOs.Billing;

public sealed class CreatePayOsPaymentDTO
{
    public Guid CheckoutId { get; set; }
    public Guid CashShiftId { get; set; }
}

public sealed class PayOsPaymentDTO
{
    public Guid PaymentId { get; set; }
    public Guid BillId { get; set; }
    public Guid CashShiftId { get; set; }
    public Guid RestaurantId { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string? ProviderOrderCode { get; set; }
    public string? ProviderPaymentLinkId { get; set; }
    public string? CheckoutUrl { get; set; }
    public string? QrCode { get; set; }
    public string? ProviderTransactionId { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public DateTimeOffset? PaidAt { get; set; }
    public long? ProviderAmountPaid { get; set; }
    public long? ProviderAmountRemaining { get; set; }
    public bool IsVerifiedZero { get; set; }
    public bool RequiresReview { get; set; }
    public bool LinkDetailsAvailable { get; set; }
}

