using Shared.Persistence.Interfaces;
using Restaurant.Common.DTOs;
using Restaurant.DAL.Models;

namespace Restaurant.DAL.Interfaces;

public interface IAreaRepository : IGenericRepository<global::Restaurant.DAL.Models.Area>
{
    Task<PageResult<global::Restaurant.DAL.Models.Area>> GetPageAsync(ListQuery query, CancellationToken ct);

    Task<bool> CodeExistsAsync(Guid restaurantId, string code, Guid exceptId, CancellationToken ct);

    Task<bool> HasTablesAsync(Guid id, CancellationToken ct);

    Task<bool> HasOpenSessionsAsync(Guid id, CancellationToken ct);

}
