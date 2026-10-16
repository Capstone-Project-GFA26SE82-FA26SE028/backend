using System.ComponentModel.DataAnnotations;

namespace ApiGateway.DTOs.Identity;

public sealed class UpdateUserDTO
{
    [Required, StringLength(100)]
    public string UserName { get; set; } = "";
    [Required, EmailAddress, StringLength(255)]
    public string Email { get; set; } = "";
    [Required, StringLength(255)]
    public string FullName { get; set; } = "";
    [StringLength(20)]
    public string? PhoneNumber { get; set; }
    [StringLength(2048)]
    public string? AvatarUrl { get; set; }
}
