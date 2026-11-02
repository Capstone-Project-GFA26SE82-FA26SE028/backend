using System.ComponentModel.DataAnnotations;

namespace ApiGateway.DTOs.Identity;

public class UpdateRoleDTO
{
    [Required(ErrorMessage = "Trường {0} là bắt buộc.")]
    [StringLength(50, ErrorMessage = "Trường {0} phải có độ dài từ {2} đến {1} ký tự.")]
    public string RoleName { get; set; } = "";

    [StringLength(2048, ErrorMessage = "Trường {0} phải có độ dài từ {2} đến {1} ký tự.")]
    public string? Description { get; set; }
}

public sealed class CreateRoleDTO : UpdateRoleDTO
{
    [Required(ErrorMessage = "Trường {0} là bắt buộc.")]
    [StringLength(50, ErrorMessage = "Trường {0} phải có độ dài từ {2} đến {1} ký tự.")]
    [RegularExpression(@"[a-zA-Z0-9_-]+", ErrorMessage = "Trường {0} không đúng định dạng.")]
    public string RoleId { get; set; } = "";
}

