using Catalog.DAL.Interfaces;
using Catalog.DAL.Models;
using Microsoft.EntityFrameworkCore;

namespace Catalog.DAL.Implements;

public sealed class ItemOptionGroupRepository(CatalogDbContext context) : IItemOptionGroupRepository
{
    public Task<List<ItemOptionGroup>> GetAllAsync(Guid itemId, CancellationToken cancellationToken)
    {
        return context.ItemOptionGroups.AsNoTracking().Include(entity => entity.OptionGroup)
            .Where(entity => entity.ItemId == itemId)
            .OrderBy(entity => entity.OptionGroup.OptionGroupName)
            .ThenBy(entity => entity.OptionGroupId)
            .ToListAsync(cancellationToken);
    }

    public Task<ItemOptionGroup?> GetByIdAsync(Guid itemId, Guid groupId, CancellationToken cancellationToken)
    {
        return context.ItemOptionGroups.Include(entity => entity.OptionGroup).SingleOrDefaultAsync(
            entity => entity.ItemId == itemId && entity.OptionGroupId == groupId, cancellationToken);
    }

    public void Create(ItemOptionGroup entity)
    {
        context.ItemOptionGroups.Add(entity);
    }

    public void Delete(ItemOptionGroup entity)
    {
        context.ItemOptionGroups.Remove(entity);
    }
}
