namespace Billing.Common.DTOs;

public sealed class CreateCashRegisterDTO
{
    public Guid RestaurantId { get; set; }
    public string Code { get; set; } = string.Empty;
}
