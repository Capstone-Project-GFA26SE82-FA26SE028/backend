using Billing.DAL.Models;
using Shared.Persistence.Interfaces;

namespace Billing.DAL.Interfaces;

public interface IPaymentRepository : IGenericRepository<Payment>
{
    Task<bool> LockIdempotencyKeyAsync(string key, CancellationToken ct);
    Task<Payment?> GetByKeyAsync(string key, CancellationToken ct);
    Task<Payment?> GetDetailsAsync(Guid id, CancellationToken ct);
}
