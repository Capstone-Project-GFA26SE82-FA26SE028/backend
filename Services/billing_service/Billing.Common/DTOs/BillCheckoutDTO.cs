namespace Billing.Common.DTOs;

public sealed class BillCheckoutDTO
{
    public Guid CheckoutId { get; set; }
    public Guid TableSessionId { get; set; }
    public Guid? BillId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? SourceHash { get; set; }
    public Guid? CashRegisterId { get; set; }
    public DateTimeOffset? SubmittedAt { get; set; }
    public Guid? PreparedByRef { get; set; }
    public DateTimeOffset? PreparedAt { get; set; }
    public DateTimeOffset? PresentedAt { get; set; }
    public bool OrderingReopened { get; set; }
}
