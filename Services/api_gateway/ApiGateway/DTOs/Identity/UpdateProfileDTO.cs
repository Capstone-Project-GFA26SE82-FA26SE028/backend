using System.ComponentModel.DataAnnotations;

namespace ApiGateway.DTOs.Identity;

public sealed class UpdateProfileDTO
{
    [Required(ErrorMessage = "Trường {0} là bắt buộc."), StringLength(255, ErrorMessage = "Trường {0} phải có độ dài từ {2} đến {1} ký tự.")] public string FullName { get; set; } = "";
    [StringLength(20, ErrorMessage = "Trường {0} phải có độ dài từ {2} đến {1} ký tự.")]
    public string? PhoneNumber { get; set; }
    [StringLength(2048, ErrorMessage = "Trường {0} phải có độ dài từ {2} đến {1} ký tự.")]
    public string? AvatarUrl { get; set; }
}
