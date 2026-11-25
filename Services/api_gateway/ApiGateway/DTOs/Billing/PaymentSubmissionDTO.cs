using Shared.Common.Pagination;

namespace ApiGateway.DTOs.Billing;

public sealed class PaymentSubmissionDTO
{
    public Guid? CheckoutId { get; set; }
    public Guid? BillId { get; set; }
    public Guid TableSessionId { get; set; }
    public Guid CashRegisterId { get; set; }
    public DateTimeOffset? SubmittedAt { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? BillCode { get; set; }
    public decimal? TotalAmount { get; set; }
    public Guid? TableId { get; set; }
    public string? TableCode { get; set; }
    public Guid? AreaId { get; set; }
    public string? AreaName { get; set; }
    public IReadOnlyList<CashierTableDTO> Tables { get; set; } = [];
    public int PartySize { get; set; }
    public DateTimeOffset? OpenedAt { get; set; }
    public DateTimeOffset? PaidAt { get; set; }
}

public sealed class CashierTableDTO
{
    public Guid TableId { get; set; }
    public string TableCode { get; set; } = string.Empty;
    public Guid AreaId { get; set; }
    public string AreaName { get; set; } = string.Empty;
}

public sealed class PaymentSubmissionPageDTO : PagedResult<PaymentSubmissionDTO>
{
    public Guid CashShiftId { get; set; }
    public decimal? TotalAmount { get; set; }
    public decimal KnownTotalAmount { get; set; }
    public int UnbilledCount { get; set; }
}
