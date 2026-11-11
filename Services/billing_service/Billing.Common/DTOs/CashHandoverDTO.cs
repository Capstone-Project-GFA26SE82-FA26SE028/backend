namespace Billing.Common.DTOs;

public sealed class CreateCashHandoverDTO
{
    public Guid CheckoutId { get; set; }
    public decimal? CashReceived { get; set; }
}

public sealed class CashHandoverDTO
{
    public Guid PaymentId { get; set; }
    public Guid BillId { get; set; }
    public Guid RestaurantId { get; set; }
    public Guid CashRegisterId { get; set; }
    public Guid CashShiftId { get; set; }
    public Guid WaiterUserRef { get; set; }
    public Guid? CashierUserRef { get; set; }
    public decimal Amount { get; set; }
    public decimal CashReceived { get; set; }
    public decimal ChangeAmount { get; set; }
    public decimal? ConfirmedAmount { get; set; }
    public string Status { get; set; } = string.Empty;
    public string PaymentStatus { get; set; } = string.Empty;
    public DateTimeOffset CollectedAt { get; set; }
    public DateTimeOffset? ConfirmedAt { get; set; }
}
