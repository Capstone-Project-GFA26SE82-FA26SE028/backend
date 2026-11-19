namespace Identity.Common.DTOs;

public sealed class AuditLogDTO
{
    public string? FullName { get; set; }
    public string? UserName { get; set; }
    public string? Email { get; set; }
    public string? AvatarUrl { get; set; }
    public Guid AuditLogId { get; set; }
    public Guid? UserId { get; set; }
    public string? Action { get; set; }
    public string? EntityName { get; set; }
    public string? EntityId { get; set; }
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
    public string? IpAddress { get; set; }
    public DateTime CreatedAt { get; set; }
}

