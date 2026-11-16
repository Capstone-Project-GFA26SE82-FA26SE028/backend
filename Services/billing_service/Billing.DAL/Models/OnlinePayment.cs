namespace Billing.DAL.Models;

public sealed class OnlinePayment
{
    public Guid OrderId { get; set; }
    public Guid RestaurantId { get; set; }
    public Guid CustomerUserId { get; set; }
    public long ProviderOrderCode { get; set; }
    public decimal Amount { get; set; }
    public string Status { get; set; } = "Pending";
    public string? LinkId { get; set; }
    public string? CheckoutUrl { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? PaidAt { get; set; }
    public string? TransactionReference { get; set; }
    public string? RefundReason { get; set; }
    public string? RefundReference { get; set; }
    public string? RefundEvidence { get; set; }
    public Guid? RefundVerifiedBy { get; set; }
    public DateTime? RefundedAt { get; set; }
}
