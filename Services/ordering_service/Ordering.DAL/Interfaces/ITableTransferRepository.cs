using Ordering.DAL.Models;

namespace Ordering.DAL.Interfaces;

public interface ITableTransferRepository
{
    void ResetTracking();
    Task<TableTransfer?> GetAsync(Guid id, CancellationToken ct);
    Task LockSessionsAsync(Guid source, Guid target, CancellationToken ct);
    Task<bool> HasPendingAsync(Guid source, Guid target, CancellationToken ct);
    void Add(TableTransfer operation);
}
