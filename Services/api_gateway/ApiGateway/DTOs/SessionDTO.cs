namespace ApiGateway.DTOs;

public sealed class SessionDTO
{
    public string UserName { get; set; } = "";
    public string Email { get; set; } = "";
    public string RoleId { get; set; } = "";
    public Guid UserSessionId { get; set; }
    public Guid UserId { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public string? DeviceType { get; set; }
    public DateTime LoggedInAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public bool IsCurrent { get; set; }
}

