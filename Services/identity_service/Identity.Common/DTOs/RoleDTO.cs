namespace Identity.Common.DTOs;

public class RoleDTO
{
    public string RoleId { get; set; }

    public string RoleName { get; set; }

    public string? Description { get; set; }

    public RoleDTO(string roleId, string roleName, string? description)
    {
        RoleId = roleId;
        RoleName = roleName;
        Description = description;
    }
}
