using System.ComponentModel.DataAnnotations;

namespace ApiGateway.DTOs.Identity;

public sealed class ChangePasswordDTO
{
    [Required(ErrorMessage = "Trường {0} là bắt buộc.")]
    public string CurrentPassword { get; set; } = "";

    [Required(ErrorMessage = "Trường {0} là bắt buộc.")]
    [MinLength(8, ErrorMessage = "Trường {0} phải có ít nhất {1} phần tử hoặc ký tự.")]
    public string NewPassword { get; set; } = "";

    [Required(ErrorMessage = "Trường {0} là bắt buộc.")]
    [Compare(nameof(NewPassword), ErrorMessage = "Trường {0} phải khớp với {1}.")]
    public string ConfirmPassword { get; set; } = "";
}
