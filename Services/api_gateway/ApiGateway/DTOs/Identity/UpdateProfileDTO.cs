using System.ComponentModel.DataAnnotations;

namespace ApiGateway.DTOs.Identity;

public sealed class UpdateProfileDTO
{
    [Required, StringLength(255)] public string FullName { get; set; } = "";
    [StringLength(20)]
    public string? PhoneNumber { get; set; }
    [StringLength(2048)]
    public string? AvatarUrl { get; set; }
}
