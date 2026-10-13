using System.ComponentModel.DataAnnotations;

namespace ApiGateway.DTOs.Identity;

public sealed class LoginDTO
{
    public string? Email { get; set; }

    public string? Password { get; set; }
}
