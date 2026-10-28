using Billing.Common.DTOs;
using Billing.DAL.Models;
using Shared.Common.Pagination;

namespace Billing.DAL.Interfaces;

public interface ITaxFeePolicyRepository
{
    Task<bool> LockCreateAsync(CancellationToken ct);

    Task<bool> EffectiveFromExistsAsync(DateTime effectiveFrom, CancellationToken ct);

    Task<int?> GetHighestVersionAsync(CancellationToken ct);

    Task AddAsync(TaxFeePolicy policy, CancellationToken ct);

    Task<TaxFeePolicy?> GetByIdAsync(Guid id, CancellationToken ct);

    Task<PagedResult<TaxFeePolicy>> GetPageAsync(TaxFeePolicyQueryDTO query, CancellationToken ct);

    Task<List<TaxFeePolicy>> GetEffectiveAsync(DateTime at, CancellationToken ct);
}
