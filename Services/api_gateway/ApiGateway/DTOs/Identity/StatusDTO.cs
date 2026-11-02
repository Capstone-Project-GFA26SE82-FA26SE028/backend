using System.ComponentModel.DataAnnotations;

namespace ApiGateway.DTOs.Identity;

public sealed class StatusDTO
{
    [Required(ErrorMessage = "Trường {0} là bắt buộc.")]
    public bool? IsActive { get; set; }
}
