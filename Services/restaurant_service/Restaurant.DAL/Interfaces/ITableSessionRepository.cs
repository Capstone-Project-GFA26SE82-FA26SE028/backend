using Restaurant.DAL.Models;
using Shared.Persistence.Interfaces;

namespace Restaurant.DAL.Interfaces;

public interface ITableSessionRepository : IGenericRepository<TableSession>
{
    Task<List<TableSession>> GetOpenForTableAsync(Guid tableId, CancellationToken ct);
}
