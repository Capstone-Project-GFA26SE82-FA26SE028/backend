using Shared.Persistence.Interfaces;
using Restaurant.Common.DTOs;
using Restaurant.DAL.Models;
using TableEntity = Restaurant.DAL.Models.Table;

namespace Restaurant.DAL.Interfaces;

public interface ITableRepository : IGenericRepository<global::Restaurant.DAL.Models.Table>
{
    Task<PageResult<global::Restaurant.DAL.Models.Table>> GetPageAsync(ListQuery query, CancellationToken ct);

    Task<bool> CodeExistsAsync(Guid restaurantId, string code, Guid exceptId, CancellationToken ct);

    Task<bool> HasOpenSessionAsync(Guid id, CancellationToken ct);

    Task<bool> HasHistoryAsync(Guid id, CancellationToken ct);

}
