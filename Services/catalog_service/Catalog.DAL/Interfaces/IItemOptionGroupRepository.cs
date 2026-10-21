using Catalog.DAL.Models;

namespace Catalog.DAL.Interfaces;

public interface IItemOptionGroupRepository
{
    Task<List<ItemOptionGroup>> GetAllAsync(Guid itemId, CancellationToken cancellationToken);

    Task<ItemOptionGroup?> GetByIdAsync(Guid itemId, Guid groupId, CancellationToken cancellationToken);

    void Create(ItemOptionGroup entity);

    void Delete(ItemOptionGroup entity);
}
