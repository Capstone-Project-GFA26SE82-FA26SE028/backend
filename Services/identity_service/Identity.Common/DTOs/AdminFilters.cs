namespace Identity.Common.DTOs;

public sealed class UserFilter
{
    public string? RoleId { get; set; }
    public bool? IsActive { get; set; }
}

public sealed class SessionFilter
{
    public string? Search { get; set; }
    public string? RoleId { get; set; }
    public string? DeviceType { get; set; }
    public string? Status { get; set; }
    public DateTime? FromUtc { get; set; }
    public DateTime? ToUtc { get; set; }

    public bool IsValid()
    {
        return Search?.Length is not > 255 && RoleId?.Length is not > 50
            && DeviceType?.Length is not > 50
            && (string.IsNullOrEmpty(Status) || Status is "active" or "expired" or "revoked")
            && (!FromUtc.HasValue || !ToUtc.HasValue || FromUtc <= ToUtc);
    }
}

public sealed class UpdateUserDTO
{
    public string UserName { get; set; } = "";
    public string Email { get; set; } = "";
    public string FullName { get; set; } = "";
    public string? PhoneNumber { get; set; }
    public string? AvatarUrl { get; set; }
}
