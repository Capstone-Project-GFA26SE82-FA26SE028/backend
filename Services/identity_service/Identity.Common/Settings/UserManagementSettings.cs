namespace Identity.Common.Settings;

public sealed class UserManagementSettings
{
    // Explicit database RoleIds. Empty configuration denies all admin operations.
    public string[] AdminRoleIds { get; set; } = [];
}
