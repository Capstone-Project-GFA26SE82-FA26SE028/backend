using Billing.DAL.Models;
using Shared.Persistence.Interfaces;
using Shared.Common.Pagination;

namespace Billing.DAL.Interfaces;

public interface ICashMovementRepository : IGenericRepository<CashMovement>
{
    Task<bool> LockIdempotencyKeyAsync(string key, CancellationToken ct);
    Task<IReadOnlyList<CashMovement>> GetByKeyAsync(string key, CancellationToken ct);
    Task<CashMovement?> GetDetailsAsync(Guid id, CancellationToken ct);
    Task<decimal> GetTotalAsync(Guid shiftId, CancellationToken ct);
    Task<PagedResult<CashMovement>> GetPageAsync(Guid shiftId, int page, int pageSize, CancellationToken ct);
    Task<bool> PaymentHasMovementAsync(Guid paymentId, CancellationToken ct);
}
