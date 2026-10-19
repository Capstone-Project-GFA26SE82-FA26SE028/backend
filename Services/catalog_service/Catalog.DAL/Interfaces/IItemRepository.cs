using Catalog.Common.DTOs;
using Catalog.DAL.Models;
using Shared.Common.Pagination;

namespace Catalog.DAL.Interfaces;

public interface IItemRepository
{
    Task<PagedResult<Item>> GetPageAsync(ItemQueryDTO query, CancellationToken cancellationToken);

    Task<Item?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<bool> NameExistsAsync(
        Guid categoryId,
        string name,
        Guid excludedId,
        CancellationToken cancellationToken);

    Task<bool> CodeExistsAsync(string code, CancellationToken cancellationToken);

    void Create(Item item);

    void SetOriginalVersion(Item item, byte[] version);

    Task<bool> HasSellingItemsAsync(
        Guid categoryId,
        string[] nonSellingAvailabilities,
        CancellationToken cancellationToken);
}
