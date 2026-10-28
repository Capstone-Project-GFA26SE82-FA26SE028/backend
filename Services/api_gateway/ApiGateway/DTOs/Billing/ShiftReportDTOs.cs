using System.Text.Json.Serialization;

namespace ApiGateway.DTOs.Billing;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class CloseCashShiftDTO
{
    public decimal? ActualCash { get; set; }
    public decimal? CashHandleOver { get; set; }
    public string? DifferenceReason { get; set; }
}

public sealed class ShiftReportDTO
{
    public Guid ShiftReportId { get; set; }
    public Guid CashShiftId { get; set; }
    public Guid CashRegisterId { get; set; }
    public Guid RestaurantId { get; set; }
    public Guid CashierUserRef { get; set; }
    public string PerformerSource { get; set; } = "ShiftOwnerDerived";
    public decimal ExpectedCash { get; set; }
    public decimal ActualCash { get; set; }
    public decimal CashHandleOver { get; set; }
    public decimal Difference { get; set; }
    public decimal RemainingCash { get; set; }
    public string? DifferenceReason { get; set; }
    public DateTimeOffset ClosedAt { get; set; }
}

public sealed class ReconciliationBlockerDTO
{
    public string Code { get; set; } = string.Empty;
    public Guid? PaymentId { get; set; }
    public Guid? CashMovementId { get; set; }
    public string Message { get; set; } = string.Empty;
}

public sealed class ShiftReconciliationDTO
{
    public Guid CashShiftId { get; set; }
    public Guid RestaurantId { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal OpeningCash { get; set; }
    public decimal MovementTotal { get; set; }
    public decimal ExpectedCash { get; set; }
    public decimal SaleTotal { get; set; }
    public decimal CashInTotal { get; set; }
    public decimal CashOutTotal { get; set; }
    public decimal BankTransferTotal { get; set; }
    public int PaymentCount { get; set; }
    public bool CanClose { get; set; }
    public List<ReconciliationBlockerDTO> Blockers { get; set; } = [];
}
