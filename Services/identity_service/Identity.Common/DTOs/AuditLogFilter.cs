namespace Identity.Common.DTOs;

public sealed class AuditLogFilter
{
    public string? Search { get; set; }
    public string? Action { get; set; }
    public DateTime? FromUtc { get; set; }
    public DateTime? ToUtc { get; set; }
}
