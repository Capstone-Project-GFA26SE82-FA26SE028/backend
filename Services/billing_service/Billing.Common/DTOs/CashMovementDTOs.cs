using Shared.Common.Pagination;

namespace Billing.Common.DTOs;

public sealed class CreateCashMovementDTO
{
    public string? Type { get; set; }
    public decimal? Amount { get; set; }
    public string? Reason { get; set; }
}

public sealed class CashMovementDTO
{
    public Guid CashMovementId { get; set; }
    public Guid CashShiftId { get; set; }
    public Guid RestaurantId { get; set; }
    public Guid? PaymentId { get; set; }
    public string Type { get; set; } = string.Empty;
    public decimal SignedAmount { get; set; }
    public string? Reason { get; set; }
    public DateTimeOffset PostedAt { get; set; }
    public Guid? PerformedByRef { get; set; }
    public string PerformerSource { get; set; } = string.Empty;
}

public sealed class CashLedgerDTO
{
    public Guid CashShiftId { get; set; }
    public Guid RestaurantId { get; set; }
    public string Currency { get; set; } = "VND";
    public decimal OpeningCash { get; set; }
    public decimal MovementTotal { get; set; }
    public decimal ExpectedCash { get; set; }
    public PagedResult<CashMovementDTO> Movements { get; set; } = new();
}
