namespace Billing.Common.DTOs;

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

public sealed record PayOsCreateCommand(long OrderCode, long Amount, string Description, DateTimeOffset ExpiresAt);
public sealed record PayOsTransactionDTO(string Reference, long Amount);
public sealed record PayOsProviderResult(long OrderCode, long Amount, string Status, string LinkId,
    long? AmountPaid, long? AmountRemaining, IReadOnlyList<PayOsTransactionDTO>? Transactions,
    string? CheckoutUrl = null, string? QrCode = null);
public sealed record PayOsWebhookDTO(long OrderCode, long Amount, string Reference, string LinkId,
    string Currency, string Code, string PayloadHash, string DedupeKey);
