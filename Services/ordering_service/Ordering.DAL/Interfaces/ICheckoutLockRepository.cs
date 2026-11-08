using Ordering.DAL.Models;

namespace Ordering.DAL.Interfaces;

public interface ICheckoutLockRepository
{
    Task LockSessionAsync(Guid sessionId, CancellationToken ct);
    Task<CheckoutLock?> GetActiveAsync(Guid sessionId, CancellationToken ct);
    Task<CheckoutLock?> GetByIdAsync(Guid id, CancellationToken ct);
    void Add(CheckoutLock value);
}
