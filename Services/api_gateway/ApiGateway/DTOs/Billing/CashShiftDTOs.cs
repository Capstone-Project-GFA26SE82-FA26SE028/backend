namespace ApiGateway.DTOs.Billing;

public sealed class OpenCashShiftDTO
{
    public decimal? OpeningCash { get; set; }
}

public sealed class CashShiftDTO
{
    public Guid CashShiftId { get; set; }
    public Guid CashRegisterId { get; set; }
    public Guid RestaurantId { get; set; }
    public Guid CashierUserRef { get; set; }
    public decimal OpeningCash { get; set; }
    public DateTimeOffset OpenedAt { get; set; }
    public string Status { get; set; } = string.Empty;
}
