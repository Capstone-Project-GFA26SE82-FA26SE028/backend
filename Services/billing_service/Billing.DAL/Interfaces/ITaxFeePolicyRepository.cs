using Billing.DAL.Models;

namespace Billing.DAL.Interfaces;

public interface ITaxFeePolicyRepository
{
    Task<List<TaxFeePolicy>> GetEffectiveAsync(DateTime at, CancellationToken ct);
}
