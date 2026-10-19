using Catalog.DAL.Interfaces;
using Catalog.DAL.Models;
using Microsoft.EntityFrameworkCore;
using Catalog.Common.DTOs;
using Shared.Common.Pagination;

namespace Catalog.DAL.Implements;

public sealed class ItemRepository(
    CatalogDbContext context) : IItemRepository
{
    public async Task<PagedResult<Item>> GetPageAsync(
        ItemQueryDTO query,
        CancellationToken cancellationToken)
    {
        var source = context.Items.AsNoTracking();
        if (query.CategoryId.HasValue)
        {
            source = source.Where(item => item.CategoryId == query.CategoryId.Value);
        }

        var search = query.Search?.Trim();
        if (!string.IsNullOrEmpty(search))
        {
            source = source.Where(item => item.ItemCode.Contains(search) || item.ItemName.Contains(search));
        }

        var availability = query.Availability?.Trim();
        if (!string.IsNullOrEmpty(availability))
        {
            source = source.Where(item => item.Availability == availability);
        }

        var totalCount = await source.CountAsync(cancellationToken);
        var items = await source
            .Include(item => item.Category)
            .OrderBy(item => item.ItemName)
            .ThenBy(item => item.ItemId)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<Item>(items, totalCount, query.Page, query.PageSize);
    }

    public Task<Item?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        return context.Items.Include(item => item.Category)
            .SingleOrDefaultAsync(item => item.ItemId == id, cancellationToken);
    }

    public Task<bool> NameExistsAsync(
        Guid categoryId,
        string name,
        Guid excludedId,
        CancellationToken cancellationToken)
    {
        return context.Items.AnyAsync(item => item.CategoryId == categoryId
            && item.ItemId != excludedId && item.ItemName == name, cancellationToken);
    }

    public Task<bool> CodeExistsAsync(string code, CancellationToken cancellationToken)
    {
        return context.Items.AnyAsync(item => item.ItemCode == code, cancellationToken);
    }

    public void Create(Item item)
    {
        context.Items.Add(item);
    }

    public void SetOriginalVersion(Item item, byte[] version)
    {
        context.Entry(item).Property(entity => entity.Version).OriginalValue = version;
    }

    public Task<bool> HasSellingItemsAsync(
        Guid categoryId,
        string[] nonSellingAvailabilities,
        CancellationToken cancellationToken)
    {
        return context.Items.AnyAsync(
            item => item.CategoryId == categoryId
                && !nonSellingAvailabilities.Contains(item.Availability),
            cancellationToken);
    }
}
