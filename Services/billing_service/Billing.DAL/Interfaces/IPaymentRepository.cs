using System.Linq.Expressions;
using Billing.DAL.Models;
using Shared.Persistence.Interfaces;

namespace Billing.DAL.Interfaces;

public interface IPaymentRepository : IGenericRepository<Payment>
{
    Task<IReadOnlyDictionary<Guid, int>> GetBillPaymentCountsAsync(Guid shiftId, CancellationToken ct, Expression<Func<Payment, bool>>? include = null);
    Task<IReadOnlyList<Payment>> GetForReconciliationAsync(Guid shiftId, CancellationToken ct);
    void ResetProviderTracking();
    Task<bool> LockProviderWorkAsync(Guid id, CancellationToken ct);
    Task<string> AllocateOrderCodeAsync(CancellationToken ct);
    Task<Payment?> GetForProviderUpdateAsync(Guid id, CancellationToken ct);
    Task<IReadOnlyList<Payment>> GetByProviderCodeAsync(string code, CancellationToken ct);
    Task<bool> LockBankReferenceAsync(string reference, CancellationToken ct);
    Task<bool> BankReferenceUsedAsync(string reference, Guid exceptId, CancellationToken ct);
    Task<(IReadOnlyList<Guid> Ids, int Total)> GetProviderBatchAsync(int skip, int take, CancellationToken ct);
    Task<bool> LockIdempotencyKeyAsync(string key, CancellationToken ct);
    Task<IReadOnlyList<Payment>> GetByPayOsRequestKeyAsync(string key, CancellationToken ct);
    Task<Payment?> GetByKeyAsync(string key, CancellationToken ct);
    Task<Payment?> GetDetailsAsync(Guid id, CancellationToken ct);
}
