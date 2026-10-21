using Catalog.Common.DTOs;
using Catalog.DAL.Models;
using Shared.Common.Pagination;

namespace Catalog.DAL.Interfaces;

public interface IOptionRepository
{
    Task<PagedResult<Option>> GetPageAsync(OptionQueryDTO query, CancellationToken cancellationToken);

    Task<Option?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<bool> NameExistsAsync(Guid groupId, string name, Guid excludedId, CancellationToken cancellationToken);

    Task<bool> HasDependenciesAsync(Guid id, CancellationToken cancellationToken);

    void Create(Option entity);

    void Delete(Option entity);
}
