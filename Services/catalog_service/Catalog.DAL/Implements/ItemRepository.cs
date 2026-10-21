using Catalog.DAL.Interfaces;
using Catalog.DAL.Models;
using Microsoft.EntityFrameworkCore;
using Catalog.Common.DTOs;
using Shared.Common.Pagination;

namespace Catalog.DAL.Implements;

public sealed class ItemRepository(
    CatalogDbContext context) : IItemRepository
{
    public Task<List<Item>> GetForOrderValidationAsync(Guid[] ids, CancellationToken cancellationToken)
    {
        return context.Items.AsNoTracking().Where(item => ids.Contains(item.ItemId))
            .Include(item => item.Category)
            .Include(item => item.Prices)
            .Include(item => item.ItemOptionGroups)
                .ThenInclude(link => link.OptionGroup)
                .ThenInclude(group => group.Options)
            .AsSplitQuery()
            .ToListAsync(cancellationToken);
    }

    public async Task<PagedResult<Item>> GetMenuPageAsync(MenuQueryDTO query, DateTime at, CancellationToken cancellationToken)
    {
        var source = MenuSource(at);
        if (query.CategoryId.HasValue)
        {
            source = source.Where(item => item.CategoryId == query.CategoryId.Value);
        }

        var search = query.Search?.Trim().Normalize().ToUpperInvariant();
        if (!string.IsNullOrEmpty(search))
        {
            source = source.Where(item => item.ItemName.ToUpper().Contains(search)
                || item.ItemCode.ToUpper().Contains(search));
        }

        var count = await source.CountAsync(cancellationToken);
        var items = await source.OrderBy(item => item.ItemName).ThenBy(item => item.ItemId)
            .Skip((query.Page - 1) * query.PageSize).Take(query.PageSize)
            .ToListAsync(cancellationToken);
        return new PagedResult<Item>(items, count, query.Page, query.PageSize);
    }

    public Task<Item?> GetMenuByIdAsync(Guid id, DateTime at, CancellationToken cancellationToken)
    {
        return MenuSource(at).SingleOrDefaultAsync(item => item.ItemId == id, cancellationToken);
    }

    private IQueryable<Item> MenuSource(DateTime at)
    {
        return context.Items.AsNoTracking()
            .Include(item => item.RecipeVersions.Where(recipe => recipe.EffectiveFrom <= at
                && (!recipe.EffectiveTo.HasValue || at < recipe.EffectiveTo)))
            .Where(item => item.Category.IsActive
                && (item.Availability == "Available" || item.Availability == "OutOfStock"))
            .Include(item => item.Category)
            .Include(item => item.Prices)
            .Include(item => item.ItemOptionGroups)
                .ThenInclude(link => link.OptionGroup)
                .ThenInclude(group => group.Options)
            .AsSplitQuery();
    }

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
