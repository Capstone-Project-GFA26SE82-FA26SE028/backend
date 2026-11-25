using Billing.Common.DTOs;
using Billing.DAL.Interfaces;
using Billing.DAL.Models;
using Microsoft.EntityFrameworkCore;
using Shared.Common.Pagination;
using Shared.Persistence.Repositories;

namespace Billing.DAL.Implements;

public class PromotionRepository : GenericRepository<Promotion, BillingDbContext>, IPromotionRepository
{
    public PromotionRepository(BillingDbContext context) : base(context)
    {
    }

    public async Task<Promotion?> GetByIdAsync(Guid id, CancellationToken ct)
    {
        return await _context.Promotions
            .Include(p => p.PromotionTargets)
            .FirstOrDefaultAsync(p => p.PromotionId == id, ct);
    }

    public async Task<PagedResult<Promotion>> GetAllAsync(PromotionQueryDTO query, CancellationToken ct)
    {
        var source = _context.Promotions
            .Include(p => p.PromotionTargets)
            .AsNoTracking();

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim().ToLower();
            source = source.Where(p => p.PromotionName.ToLower().Contains(search));
        }

        if (query.IsActive.HasValue)
        {
            source = source.Where(p => p.IsActive == query.IsActive.Value);
        }

        var total = await source.CountAsync(ct);
        var page = query.Page <= 0 ? 1 : query.Page;
        var pageSize = query.PageSize <= 0 ? 10 : query.PageSize;

        var items = await source
            .OrderByDescending(p => p.StartAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return new PagedResult<Promotion>(items, total, page, pageSize);
    }

    public async Task<List<Promotion>> GetActivePromotionsAsync(DateTime now, CancellationToken ct)
    {
        return await _context.Promotions
            .Include(p => p.PromotionTargets)
            .AsNoTracking()
            .Where(p => p.IsActive && p.StartAt <= now && (!p.EndAt.HasValue || p.EndAt.Value >= now))
            .OrderByDescending(p => p.DiscountValue)
            .ToListAsync(ct);
    }

    public async Task<int> GetUsageCountAsync(Guid promotionId, CancellationToken ct)
    {
        return await _context.BillAdjustments
            .AsNoTracking()
            .CountAsync(a => a.PromotionId == promotionId, ct);
    }

    public async Task AddAsync(Promotion promotion, CancellationToken ct)
    {
        await _context.Promotions.AddAsync(promotion, ct);
    }

    public Task UpdateAsync(Promotion promotion, CancellationToken ct)
    {
        _context.Promotions.Update(promotion);
        return Task.CompletedTask;
    }
}
