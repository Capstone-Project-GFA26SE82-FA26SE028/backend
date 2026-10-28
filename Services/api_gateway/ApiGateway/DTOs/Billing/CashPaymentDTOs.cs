using System.Text.Json.Serialization;

namespace ApiGateway.DTOs.Billing;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class CreateCashPaymentDTO
{
    public Guid CheckoutId { get; set; }
    public Guid CashShiftId { get; set; }
    public decimal? CashReceived { get; set; }
}

public sealed class PaymentDTO
{
    public Guid PaymentId { get; set; }
    public Guid BillId { get; set; }
    public string BillNumber { get; set; } = string.Empty;
    public Guid RestaurantId { get; set; }
    public Guid CheckoutId { get; set; }
    public Guid CashShiftId { get; set; }
    public Guid CashRegisterId { get; set; }
    public string MethodCode { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Currency { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public decimal CashReceived { get; set; }
    public decimal ChangeAmount { get; set; }
    public Guid ConfirmedByRef { get; set; }
    public DateTimeOffset PaidAt { get; set; }
    public Guid CashMovementId { get; set; }
}
