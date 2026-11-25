using Billing.Common.DTOs;
using Billing.DAL.Interfaces;
using Billing.DAL.Models;
using Microsoft.EntityFrameworkCore;
using Shared.Common.Pagination;
using Shared.Persistence.Repositories;

namespace Billing.DAL.Implements;

public class VoucherRepository : GenericRepository<Voucher, BillingDbContext>, IVoucherRepository
{
    public VoucherRepository(BillingDbContext context) : base(context)
    {
    }

    public async Task<Voucher?> GetByIdAsync(Guid id, CancellationToken ct)
    {
        return await _context.Vouchers
            .FirstOrDefaultAsync(v => v.VoucherId == id, ct);
    }

    public async Task<Voucher?> FindByCodeAsync(string normalizedCode, CancellationToken ct)
    {
        return await _context.Vouchers
            .FirstOrDefaultAsync(v => v.NormalizedVoucherCode == normalizedCode, ct);
    }

    public async Task<PagedResult<Voucher>> GetAllAsync(VoucherQueryDTO query, CancellationToken ct)
    {
        var source = _context.Vouchers.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim().ToUpper();
            source = source.Where(v => v.NormalizedVoucherCode.Contains(search));
        }

        if (query.IsActive.HasValue)
        {
            source = source.Where(v => v.IsActive == query.IsActive.Value);
        }

        var total = await source.CountAsync(ct);
        var page = query.Page <= 0 ? 1 : query.Page;
        var pageSize = query.PageSize <= 0 ? 10 : query.PageSize;

        var items = await source
            .OrderByDescending(v => v.StartAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return new PagedResult<Voucher>(items, total, page, pageSize);
    }

    public async Task<List<Voucher>> GetActiveVouchersAsync(DateTime now, CancellationToken ct)
    {
        return await _context.Vouchers
            .AsNoTracking()
            .Where(v => v.IsActive && v.StartAt <= now && (!v.EndAt.HasValue || v.EndAt.Value >= now))
            .OrderByDescending(v => v.DiscountValue)
            .ToListAsync(ct);
    }

    public async Task<int> GetUsageCountAsync(Guid voucherId, CancellationToken ct)
    {
        return await _context.VoucherRedemptions
            .AsNoTracking()
            .CountAsync(r => r.VoucherId == voucherId && (r.Status == "Committed" || r.Status == "Reserved"), ct);
    }

    public async Task AddAsync(Voucher voucher, CancellationToken ct)
    {
        await _context.Vouchers.AddAsync(voucher, ct);
    }

    public Task UpdateAsync(Voucher voucher, CancellationToken ct)
    {
        _context.Vouchers.Update(voucher);
        return Task.CompletedTask;
    }
}
