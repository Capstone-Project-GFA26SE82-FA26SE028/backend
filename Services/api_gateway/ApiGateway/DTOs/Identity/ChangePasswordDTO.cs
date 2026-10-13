using System.ComponentModel.DataAnnotations;

namespace ApiGateway.DTOs.Identity;

public sealed class ChangePasswordDTO
{
    [Required]
    public string CurrentPassword { get; set; } = "";

    [Required]
    [MinLength(8)]
    public string NewPassword { get; set; } = "";

    [Required]
    [Compare(nameof(NewPassword))]
    public string ConfirmPassword { get; set; } = "";
}
