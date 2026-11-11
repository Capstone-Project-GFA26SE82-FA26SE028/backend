using Restaurant.DAL.Models;
using Shared.Persistence.Interfaces;

namespace Restaurant.DAL.Interfaces;

public interface ITableSessionRepository : IGenericRepository<TableSession>
{
    Task<TableSession?> GetForDepartureAsync(Guid id, CancellationToken ct);

    Task<TableSession?> GetForBillingAsync(Guid id, CancellationToken ct);

    Task<TableSession?> GetForOrderingAsync(Guid id, CancellationToken ct);

    Task<TableSession?> LockAsync(Guid id, CancellationToken ct);

    Task<List<TableSession>> GetOpenForTableAsync(Guid tableId, CancellationToken ct);
}
