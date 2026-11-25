using Restaurant.DAL.Models;
using Shared.Persistence.Interfaces;

namespace Restaurant.DAL.Interfaces;

public interface ITableSessionRepository : IGenericRepository<TableSession>
{
    Task<TableSession?> LockForTransferAsync(Guid id, CancellationToken ct);
    Task<TableSession?> GetForBillingAsync(Guid id, CancellationToken ct);
    Task<List<TableSession>> GetBillingDisplaysAsync(Guid restaurantId, IReadOnlyList<Guid> ids, CancellationToken ct);

    Task<TableSession?> GetForOrderingAsync(Guid id, CancellationToken ct);

    Task<TableSession?> LockAsync(Guid id, CancellationToken ct);

    Task<TableSession?> LockWithActiveTablesAsync(Guid id, CancellationToken ct);

    Task<List<SessionTable>> GetUnreleasedLinksAsync(Guid id, CancellationToken ct);

    Task<List<TableSession>> GetOpenForTableAsync(Guid tableId, CancellationToken ct);
}
