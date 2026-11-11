using Billing.DAL.Models;
using Shared.Common.Pagination;

namespace Billing.DAL.Interfaces;

public interface ICashHandoverRepository
{
    Task<CashHandover?> GetByIdAsync(Guid paymentId, CancellationToken ct);
    Task<PagedResult<CashHandover>> GetPageAsync(Guid shiftId, int page, int pageSize, CancellationToken ct);
    void Add(CashHandover value);
}
