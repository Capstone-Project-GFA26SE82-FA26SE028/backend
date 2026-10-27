using Billing.Common.DTOs;
using Billing.DAL.Models;
using Shared.Common.Pagination;
using Shared.Persistence.Interfaces;

namespace Billing.DAL.Interfaces;

public interface IBillRepository : IGenericRepository<Bill>
{
    Task<bool> LockSessionAsync(Guid sessionId, CancellationToken ct);
    Task<bool> SessionHasBillAsync(Guid sessionId, CancellationToken ct);
    Task<Bill?> GetDetailsAsync(Guid id, CancellationToken ct);
    Task<PagedResult<Bill>> GetPageAsync(BillQueryDTO query, IReadOnlyList<Guid>? restaurantIds, CancellationToken ct);
}
