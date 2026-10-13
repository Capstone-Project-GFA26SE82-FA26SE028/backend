using System.ComponentModel.DataAnnotations;

namespace ApiGateway.DTOs.Identity;

public sealed class RefreshDTO
{
    public string? RefreshToken { get; set; }
}
