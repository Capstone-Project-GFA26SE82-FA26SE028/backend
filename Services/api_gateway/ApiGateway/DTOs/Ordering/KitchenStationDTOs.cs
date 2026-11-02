using System.ComponentModel.DataAnnotations;

namespace ApiGateway.DTOs.Ordering;

public sealed class KitchenStationWriteDTO
{
    [Required(ErrorMessage = "Trường {0} là bắt buộc.")]
    public string KitchenStationCode { get; set; } = string.Empty;
    [Required(ErrorMessage = "Trường {0} là bắt buộc.")]
    public string KitchenStationName { get; set; } = string.Empty;
}

public sealed class KitchenStationStatusDTO
{
    [Required(ErrorMessage = "Trường {0} là bắt buộc.")]
    public bool? IsActive { get; set; }
}

public sealed class KitchenStationDTO
{
    public Guid KitchenStationId { get; set; }
    public string KitchenStationCode { get; set; } = string.Empty;
    public string KitchenStationName { get; set; } = string.Empty;
    public bool IsActive { get; set; }
}
