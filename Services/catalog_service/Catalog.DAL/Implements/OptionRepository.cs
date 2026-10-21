using Catalog.Common.DTOs;
using Catalog.DAL.Interfaces;
using Catalog.DAL.Models;
using Microsoft.EntityFrameworkCore;
using Shared.Common.Pagination;

namespace Catalog.DAL.Implements;

public sealed class OptionRepository(CatalogDbContext context) : IOptionRepository
{
    public async Task<PagedResult<Option>> GetPageAsync(OptionQueryDTO query, CancellationToken cancellationToken)
    {
        var source = context.Options.AsNoTracking();
        if (query.OptionGroupId.HasValue)
        {
            source = source.Where(entity => entity.OptionGroupId == query.OptionGroupId.Value);
        }

        if (!string.IsNullOrWhiteSpace(query.Availability))
        {
            source = source.Where(entity => entity.Availability == query.Availability);
        }

        var search = query.Search?.Trim();
        if (!string.IsNullOrEmpty(search))
        {
            source = source.Where(entity => entity.OptionName.Contains(search));
        }

        var count = await source.CountAsync(cancellationToken);
        var entities = await source.OrderBy(entity => entity.OptionName)
            .ThenBy(entity => entity.OptionId)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(cancellationToken);
        return new PagedResult<Option>(entities, count, query.Page, query.PageSize);
    }

    public Task<Option?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        return context.Options.SingleOrDefaultAsync(entity => entity.OptionId == id, cancellationToken);
    }

    public Task<bool> NameExistsAsync(Guid groupId, string name, Guid excludedId, CancellationToken cancellationToken)
    {
        return context.Options.AnyAsync(entity => entity.OptionGroupId == groupId && entity.OptionId != excludedId
            && entity.OptionName == name, cancellationToken);
    }

    public async Task<bool> HasDependenciesAsync(Guid id, CancellationToken cancellationToken)
    {
        return await context.RecipeVersions.AnyAsync(entity => entity.OptionId == id, cancellationToken)
            || await context.ItemOptionGroups.AnyAsync(link => context.Options.Any(option =>
                option.OptionId == id && option.OptionGroupId == link.OptionGroupId), cancellationToken);
    }

    public void Create(Option entity)
    {
        context.Options.Add(entity);
    }

    public void Delete(Option entity)
    {
        context.Options.Remove(entity);
    }
}
