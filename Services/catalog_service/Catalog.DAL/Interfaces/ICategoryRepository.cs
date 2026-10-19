using Catalog.Common.DTOs;
using Catalog.DAL.Models;
using Shared.Common.Pagination;

namespace Catalog.DAL.Interfaces;

public interface ICategoryRepository
{
    Task<PagedResult<CategoryDTO>> GetPageAsync(
        CategoryQueryDTO query,
        CancellationToken cancellationToken);

    Task<Category?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken);

    Task<bool> NameExistsAsync(
        string name,
        Guid excludedId,
        CancellationToken cancellationToken);

    Task<bool> CodeExistsAsync(
        string code,
        CancellationToken cancellationToken);

    Task<bool> HasItemsAsync(
        Guid categoryId,
        CancellationToken cancellationToken);

    void Create(Category category);

    void Delete(Category category);
}
