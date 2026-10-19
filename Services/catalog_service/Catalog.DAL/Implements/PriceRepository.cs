using System.Data.SqlTypes;
using Catalog.Common.DTOs;
using Catalog.DAL.Interfaces;
using Catalog.DAL.Models;
using Microsoft.EntityFrameworkCore;
using Shared.Common.Pagination;

namespace Catalog.DAL.Implements;

public sealed class PriceRepository(CatalogDbContext context) : IPriceRepository
{
    public async Task<PagedResult<Price>> GetPageAsync(PriceQueryDTO query, CancellationToken cancellationToken)
    {
        var source = context.Prices.AsNoTracking();
        if (query.ItemId.HasValue)
        {
            source = source.Where(price => price.ItemId == query.ItemId.Value);
        }

        if (!string.IsNullOrWhiteSpace(query.Channel))
        {
            source = source.Where(price => price.Channel == query.Channel);
        }

        var count = await source.CountAsync(cancellationToken);
        var prices = await source.OrderByDescending(price => price.EffectiveFrom)
            .ThenBy(price => price.PriceId)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(cancellationToken);
        return new PagedResult<Price>(prices, count, query.Page, query.PageSize);
    }

    public Task<Price?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        return context.Prices.SingleOrDefaultAsync(price => price.PriceId == id, cancellationToken);
    }

    public Task<Guid?> GetItemIdAsync(Guid id, CancellationToken cancellationToken)
    {
        return context.Prices.Where(price => price.PriceId == id)
            .Select(price => (Guid?)price.ItemId).SingleOrDefaultAsync(cancellationToken);
    }

    public Task<List<Price>> GetForItemAsync(Guid itemId, CancellationToken cancellationToken)
    {
        return context.Prices.AsNoTracking().Where(price => price.ItemId == itemId).ToListAsync(cancellationToken);
    }

    public DateTime NormalizeTimestamp(DateTime utc)
    {
        return DateTime.SpecifyKind(new SqlDateTime(utc).Value, DateTimeKind.Utc);
    }

    public void Create(Price price)
    {
        context.Prices.Add(price);
    }

    public void Delete(Price price)
    {
        context.Prices.Remove(price);
    }
}
