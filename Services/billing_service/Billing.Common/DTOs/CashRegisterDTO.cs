namespace Billing.Common.DTOs;

public sealed class CashRegisterDTO
{
    public Guid CashRegisterId { get; set; }
    public Guid RestaurantId { get; set; }
    public string Code { get; set; } = string.Empty;
}
