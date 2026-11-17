using Microsoft.EntityFrameworkCore;

namespace Shared.Persistence.Notifications;

public sealed class NotificationOutboxRepository<TContext>(TContext db) where TContext : DbContext
{
    public Task<List<NotificationOutbox>> GetPendingAsync(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        return db.Set<NotificationOutbox>().AsNoTracking().Where(value => value.ProcessedAt == null && value.NextAttemptAt <= now)
            .OrderBy(value => value.NextAttemptAt).ThenBy(value => value.CreatedAt).Take(50).ToListAsync(ct);
    }

    public Task<int> CompleteAsync(Guid id, CancellationToken ct)
    {
        return db.Set<NotificationOutbox>().Where(value => value.NotificationOutboxId == id && value.ProcessedAt == null)
            .ExecuteUpdateAsync(set => set.SetProperty(value => value.ProcessedAt, (DateTime?)DateTime.UtcNow)
                .SetProperty(value => value.LastError, (string?)null), ct);
    }

    public Task<int> RetryAsync(Guid id, int attempts, string error, CancellationToken ct)
    {
        var next = DateTime.UtcNow.AddSeconds(Math.Min(300, 5 * Math.Pow(2, Math.Min(attempts, 6))));
        return db.Set<NotificationOutbox>().Where(value => value.NotificationOutboxId == id && value.ProcessedAt == null)
            .ExecuteUpdateAsync(set => set.SetProperty(value => value.Attempts, value => value.Attempts + 1)
                .SetProperty(value => value.NextAttemptAt, next).SetProperty(value => value.LastError, error), ct);
    }
}
