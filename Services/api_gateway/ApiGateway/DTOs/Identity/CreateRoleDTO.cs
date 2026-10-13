using System.ComponentModel.DataAnnotations;

namespace ApiGateway.DTOs.Identity;

public class UpdateRoleDTO
{
    [Required]
    [StringLength(50)]
    public string RoleName { get; set; } = "";

    [StringLength(2048)]
    public string? Description { get; set; }
}

public sealed class CreateRoleDTO : UpdateRoleDTO
{
    [Required]
    [StringLength(50)]
    [RegularExpression(@"[a-zA-Z0-9_-]+")]
    public string RoleId { get; set; } = "";
}

