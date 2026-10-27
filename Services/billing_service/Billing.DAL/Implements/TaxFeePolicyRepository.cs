using Billing.DAL.Interfaces;
using Billing.DAL.Models;
using Microsoft.EntityFrameworkCore;

namespace Billing.DAL.Implements;

public sealed class TaxFeePolicyRepository(BillingDbContext db) : ITaxFeePolicyRepository
{
    public async Task<List<TaxFeePolicy>> GetEffectiveAsync(DateTime at, CancellationToken ct)
    {
        var latest = await db.TaxFeePolicies.Where(policy => policy.EffectiveFrom <= at)
            .MaxAsync(policy => (DateTime?)policy.EffectiveFrom, ct);
        if (!latest.HasValue)
        {
            return [];
        }

        return await db.TaxFeePolicies.AsNoTracking()
            .Where(policy => policy.EffectiveFrom == latest.Value).OrderByDescending(policy => policy.Version)
            .Take(2).ToListAsync(ct);
    }
}
