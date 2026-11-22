using Shared.Common.Pagination;

namespace Billing.Common.DTOs;

public sealed class PaymentSubmissionQueryDTO
{
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public string? Search { get; set; }
    public Guid? AreaId { get; set; }
    public Guid? CashShiftId { get; set; }

    public bool IsValid()
    {
        return new PaginationRequest
        {
            Page = Page,
            PageSize = PageSize
        }.IsValid() && AreaId != Guid.Empty && CashShiftId != Guid.Empty
            && Search?.Length is not > 50 && Search?.Any(char.IsControl) != true;
    }
}

public sealed class PaymentSubmissionPageDTO : PagedResult<PaymentSubmissionDTO>
{
    public Guid CashShiftId { get; set; }
    public decimal? TotalAmount { get; set; }
    public decimal KnownTotalAmount { get; set; }
    public int UnbilledCount { get; set; }
}

public sealed record BillingTableDisplayDTO(Guid TableId, string TableCode, Guid AreaId, string AreaName);

public sealed record BillingSessionDisplayDTO(Guid TableSessionId, Guid RestaurantId, int PartySize,
    DateTimeOffset OpenedAt, IReadOnlyList<BillingTableDisplayDTO> Tables);
