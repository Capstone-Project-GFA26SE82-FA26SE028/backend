using Catalog.Common.DTOs;
using Catalog.DAL.Models;
using Shared.Common.Pagination;

namespace Catalog.DAL.Interfaces;

public interface IPriceRepository
{
    Task<PagedResult<Price>> GetPageAsync(PriceQueryDTO query, CancellationToken cancellationToken);

    Task<Price?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<Guid?> GetItemIdAsync(Guid id, CancellationToken cancellationToken);

    Task<List<Price>> GetForItemAsync(Guid itemId, CancellationToken cancellationToken);

    DateTime NormalizeTimestamp(DateTime utc);

    void Create(Price price);

    void Delete(Price price);
}
