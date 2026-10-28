namespace Billing.Common.DTOs;

public sealed class CreateCashPaymentDTO
{
    public Guid CheckoutId { get; set; }
    public Guid CashShiftId { get; set; }
    public decimal? CashReceived { get; set; }
}
