using System.ComponentModel.DataAnnotations;

namespace ApiGateway.DTOs.Identity;

public sealed class CreateUserDTO
{
    public string? AvatarUrl { get; set; }
    [Required(ErrorMessage = "Trường {0} là bắt buộc."), StringLength(100, ErrorMessage = "Trường {0} phải có độ dài từ {2} đến {1} ký tự.")] public string UserName { get; set; } = "";
    [Required(ErrorMessage = "Trường {0} là bắt buộc."), EmailAddress(ErrorMessage = "Trường {0} phải là địa chỉ email hợp lệ."), StringLength(255, ErrorMessage = "Trường {0} phải có độ dài từ {2} đến {1} ký tự.")] public string Email { get; set; } = "";
    [Required(ErrorMessage = "Trường {0} là bắt buộc."), StringLength(72, MinimumLength = 8, ErrorMessage = "Trường {0} phải có độ dài từ {2} đến {1} ký tự."), DataType(DataType.Password)] public string Password { get; set; } = "";
    [Required(ErrorMessage = "Trường {0} là bắt buộc."), StringLength(255, ErrorMessage = "Trường {0} phải có độ dài từ {2} đến {1} ký tự.")] public string FullName { get; set; } = "";
    [StringLength(20, ErrorMessage = "Trường {0} phải có độ dài từ {2} đến {1} ký tự.")]
    public string? PhoneNumber { get; set; }
    [Required(ErrorMessage = "Trường {0} là bắt buộc."), StringLength(50, ErrorMessage = "Trường {0} phải có độ dài từ {2} đến {1} ký tự.")] public string RoleId { get; set; } = "";
}
