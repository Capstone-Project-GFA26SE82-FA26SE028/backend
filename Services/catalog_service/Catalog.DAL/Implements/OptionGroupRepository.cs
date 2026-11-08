using Catalog.Common.DTOs;
using Catalog.DAL.Interfaces;
using Catalog.DAL.Models;
using Microsoft.EntityFrameworkCore;
using Shared.Common.Pagination;

namespace Catalog.DAL.Implements;

public sealed class OptionGroupRepository(CatalogDbContext context) : IOptionGroupRepository
{
    public async Task<PagedResult<OptionGroup>> GetPageAsync(OptionGroupQueryDTO query, CancellationToken cancellationToken)
    {
        var source = context.OptionGroups.AsNoTracking();

        var search = query.Search?.Trim();
        if (!string.IsNullOrEmpty(search))
        {
            source = source.Where(entity => entity.OptionGroupName.Contains(search));
        }

        var count = await source.CountAsync(cancellationToken);
        var entities = await source.OrderBy(entity => entity.OptionGroupName)
            .ThenBy(entity => entity.OptionGroupId)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(cancellationToken);
        return new PagedResult<OptionGroup>(entities, count, query.Page, query.PageSize);
    }

    public Task<OptionGroup?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        return context.OptionGroups.SingleOrDefaultAsync(entity => entity.OptionGroupId == id, cancellationToken);
    }

    public Task<bool> NameExistsAsync(string name, Guid excludedId, CancellationToken cancellationToken)
    {
        return context.OptionGroups.AnyAsync(entity => entity.OptionGroupId != excludedId
            && entity.OptionGroupName == name, cancellationToken);
    }

    public async Task<bool> HasDependenciesAsync(Guid id, CancellationToken cancellationToken)
    {
        return await context.Options.AnyAsync(entity => entity.OptionGroupId == id, cancellationToken)
            || await context.ItemOptionGroups.AnyAsync(entity => entity.OptionGroupId == id, cancellationToken);
    }

    public void Create(OptionGroup entity)
    {
        context.OptionGroups.Add(entity);
    }

    public void Delete(OptionGroup entity)
    {
        context.OptionGroups.Remove(entity);
    }
}
