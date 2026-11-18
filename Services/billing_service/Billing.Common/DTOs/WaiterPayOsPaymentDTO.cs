namespace Billing.Common.DTOs;

public sealed class WaiterPayOsPaymentDTO
{
    public Guid PaymentId { get; set; }
    public Guid BillId { get; set; }
    public Guid TableSessionId { get; set; }
    public Guid CheckoutId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string BillStatus { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string? CheckoutUrl { get; set; }
    public string? QrCode { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public DateTimeOffset? PaidAt { get; set; }
    public bool CanPresentQr { get; set; }
    public bool PaymentConfirmed { get; set; }
    public bool RequiresReview { get; set; }
    public DateTimeOffset EvaluatedAt { get; set; }
}
