using System.ComponentModel.DataAnnotations;

namespace ApiGateway.DTOs.Identity;

public sealed class CreateUserDTO
{
    [Required, StringLength(100)] public string UserName { get; set; } = "";
    [Required, EmailAddress, StringLength(255)] public string Email { get; set; } = "";
    [Required, StringLength(72, MinimumLength = 8), DataType(DataType.Password)] public string Password { get; set; } = "";
    [Required, StringLength(255)] public string FullName { get; set; } = "";
    [StringLength(20)]
    public string? PhoneNumber { get; set; }
    [Required, StringLength(50)] public string RoleId { get; set; } = "";
}
