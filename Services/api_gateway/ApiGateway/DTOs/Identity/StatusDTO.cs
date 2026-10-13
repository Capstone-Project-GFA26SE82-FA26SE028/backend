using System.ComponentModel.DataAnnotations;

namespace ApiGateway.DTOs.Identity;

public sealed class StatusDTO
{
    [Required]
    public bool? IsActive { get; set; }
}
