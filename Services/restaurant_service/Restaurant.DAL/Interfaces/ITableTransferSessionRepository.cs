using Restaurant.DAL.Models;

namespace Restaurant.DAL.Interfaces;

public interface ITableTransferSessionRepository
{
    Task<TableTransferSession?> GetAsync(Guid id, CancellationToken ct);
    Task LockAsync(Guid id, CancellationToken ct);
    void Add(TableTransferSession value);
    Task RevokeGuestsAsync(Guid sessionId, DateTime now, CancellationToken ct);
}
