namespace Billing.Common.DTOs;

public sealed record PaymentSubmissionDTO(Guid? CheckoutId, Guid TableSessionId, Guid CashRegisterId,
    DateTimeOffset? SubmittedAt, string Status, Guid? BillId = null)
{
    public string? BillCode { get; init; }
    public decimal? TotalAmount { get; init; }
    public DateTimeOffset? PaidAt { get; init; }
    public DateTimeOffset? OpenedAt { get; init; }
    public int PartySize { get; init; }
    public IReadOnlyList<BillingTableDisplayDTO> Tables { get; init; } = [];
    public Guid? TableId => Tables.Count == 1 ? Tables[0].TableId : null;
    public string? TableCode => Tables.Count == 1 ? Tables[0].TableCode : null;
    public Guid? AreaId => Tables.Count > 0 && Tables.Select(table => table.AreaId).Distinct().Count() == 1
        ? Tables[0].AreaId : null;
    public string? AreaName => AreaId.HasValue ? Tables[0].AreaName : null;
}
