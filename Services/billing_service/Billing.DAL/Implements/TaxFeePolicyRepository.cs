using System.Data;
using Billing.Common.DTOs;
using Billing.DAL.Interfaces;
using Billing.DAL.Models;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Shared.Common.Pagination;

namespace Billing.DAL.Implements;

public sealed class TaxFeePolicyRepository(BillingDbContext db) : ITaxFeePolicyRepository
{
    public async Task<bool> LockCreateAsync(CancellationToken ct)
    {
        if (db.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("Cần có giao dịch đang hoạt động để khóa thao tác tạo chính sách thuế.");
        }

        if (!db.Database.IsSqlServer())
        {
            return true;
        }

        var result = new SqlParameter("@result", SqlDbType.Int)
        {
            Direction = ParameterDirection.Output
        };
        await db.Database.ExecuteSqlRawAsync(
            "EXEC @result = sys.sp_getapplock @Resource = N'Billing.TaxFeePolicy.Create', @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 10000;",
            [result], ct);
        return (int)result.Value >= 0;
    }

    public Task<bool> EffectiveFromExistsAsync(DateTime effectiveFrom, CancellationToken ct)
    {
        return db.TaxFeePolicies.AnyAsync(policy => policy.EffectiveFrom == effectiveFrom, ct);
    }

    public Task<int?> GetHighestVersionAsync(CancellationToken ct)
    {
        return db.TaxFeePolicies.MaxAsync(policy => (int?)policy.Version, ct);
    }

    public async Task AddAsync(TaxFeePolicy policy, CancellationToken ct)
    {
        await db.TaxFeePolicies.AddAsync(policy, ct);
    }

    public Task<TaxFeePolicy?> GetByIdAsync(Guid id, CancellationToken ct)
    {
        return db.TaxFeePolicies.AsNoTracking().SingleOrDefaultAsync(policy => policy.TaxFeePolicyId == id, ct);
    }

    public async Task<PagedResult<TaxFeePolicy>> GetPageAsync(TaxFeePolicyQueryDTO query, CancellationToken ct)
    {
        var source = db.TaxFeePolicies.AsNoTracking();
        var total = await source.CountAsync(ct);
        var items = await source.OrderByDescending(policy => policy.EffectiveFrom)
            .ThenByDescending(policy => policy.Version).ThenBy(policy => policy.TaxFeePolicyId)
            .Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToListAsync(ct);
        return new PagedResult<TaxFeePolicy>(items, total, query.Page, query.PageSize);
    }

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
