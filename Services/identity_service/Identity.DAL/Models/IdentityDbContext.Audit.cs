using System.Text.Json;

namespace Identity.Domain.Models;

public partial class IdentityDbContext
{
    public string? AuditIpAddress { get; set; }

    public void RecordAudit(Guid? actorId, string action, string entityName, string? entityId,
        object? oldValue = null, object? newValue = null)
    {
        AuditLogs.Add(new AuditLog
        {
            AuditLogId = Guid.NewGuid(),
            UserId = actorId,
            Action = action,
            EntityName = entityName,
            EntityId = entityId!,
            OldValue = oldValue is null ? null! : JsonSerializer.Serialize(oldValue),
            NewValue = newValue is null ? null! : JsonSerializer.Serialize(newValue),
            IpAddress = AuditIpAddress!,
            CreatedAt = DateTime.UtcNow
        });
    }
}
