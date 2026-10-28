using Billing.DAL.Models;
using Shared.Persistence.Interfaces;

namespace Billing.DAL.Interfaces;

public interface IPaymentRepository : IGenericRepository<Payment>
{
    Task<IReadOnlyDictionary<Guid, int>> GetBillPaymentCountsAsync(Guid shiftId, CancellationToken ct);
    Task<IReadOnlyList<Payment>> GetForReconciliationAsync(Guid shiftId, CancellationToken ct);
    Task<bool> LockIdempotencyKeyAsync(string key, CancellationToken ct);
    Task<Payment?> GetByKeyAsync(string key, CancellationToken ct);
    Task<Payment?> GetDetailsAsync(Guid id, CancellationToken ct);
}
