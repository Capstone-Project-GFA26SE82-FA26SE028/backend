using Shared.Persistence.Interfaces;
using Restaurant.Common.DTOs;
using Restaurant.DAL.Models;

namespace Restaurant.DAL.Interfaces;

public interface IQrRepository : IGenericRepository<global::Restaurant.DAL.Models.Qr>
{
    Task<PageResult<global::Restaurant.DAL.Models.Qr>> GetPageAsync(ListQuery query, CancellationToken ct);

    Task<List<Qr>> GetActiveAsync(Guid tableId, CancellationToken ct);

    Task<Qr?> FindByHashAsync(string hash, CancellationToken ct);

}
