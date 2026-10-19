using Catalog.Common.DTOs;
using Catalog.DAL.Models;
using Shared.Common.Pagination;

namespace Catalog.DAL.Interfaces;

public interface IOptionGroupRepository
{
    Task<PagedResult<OptionGroup>> GetPageAsync(OptionGroupQueryDTO query, CancellationToken cancellationToken);

    Task<OptionGroup?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<bool> NameExistsAsync(string name, Guid excludedId, CancellationToken cancellationToken);

    Task<bool> HasDependenciesAsync(Guid id, CancellationToken cancellationToken);

    void Create(OptionGroup entity);

    void Delete(OptionGroup entity);
}
