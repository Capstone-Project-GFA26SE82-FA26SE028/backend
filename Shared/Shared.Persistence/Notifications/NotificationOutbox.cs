using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Shared.Common.Notifications;

namespace Shared.Persistence.Notifications;

public sealed class NotificationOutbox
{
    public Guid NotificationOutboxId { get; set; }
    public Guid RestaurantId { get; set; }
    public string EventKey { get; set; } = "";
    public string Payload { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime NextAttemptAt { get; set; }
    public DateTime? ProcessedAt { get; set; }
    public int Attempts { get; set; }
    public string? LastError { get; set; }
}

public static class NotificationOutboxModel
{
    public static void Configure(ModelBuilder builder)
    {
        builder.Entity<NotificationOutbox>(entity =>
        {
            entity.ToTable("NotificationOutbox");
            entity.HasKey(value => value.NotificationOutboxId);
            entity.Property(value => value.EventKey).HasMaxLength(200);
            entity.Property(value => value.LastError).HasMaxLength(100);
            entity.HasIndex(value => value.EventKey).IsUnique();
            entity.HasIndex(value => new { value.ProcessedAt, value.NextAttemptAt });
        });
    }

    public static async Task Enqueue(DbContext db, NotificationEvent value, CancellationToken ct)
    {
        if (value.RestaurantId == Guid.Empty
            || db.Set<NotificationOutbox>().Local.Any(row => row.EventKey == value.EventKey)
            || await db.Set<NotificationOutbox>().AnyAsync(row => row.EventKey == value.EventKey, ct))
        {
            return;
        }

        value.OccurredAt = DateTime.UtcNow;
        db.Set<NotificationOutbox>().Add(new NotificationOutbox
        {
            NotificationOutboxId = Guid.NewGuid(),
            RestaurantId = value.RestaurantId,
            EventKey = value.EventKey,
            Payload = JsonSerializer.Serialize(value),
            CreatedAt = value.OccurredAt,
            NextAttemptAt = value.OccurredAt
        });
    }
}
