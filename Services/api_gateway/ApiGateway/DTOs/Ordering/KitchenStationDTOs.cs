using System.ComponentModel.DataAnnotations;

namespace ApiGateway.DTOs.Ordering;

public sealed class KitchenStationWriteDTO
{
    [Required]
    public string KitchenStationCode { get; set; } = string.Empty;
    [Required]
    public string KitchenStationName { get; set; } = string.Empty;
}

public sealed class KitchenStationStatusDTO
{
    [Required]
    public bool? IsActive { get; set; }
}

public sealed class KitchenStationDTO
{
    public Guid KitchenStationId { get; set; }
    public string KitchenStationCode { get; set; } = string.Empty;
    public string KitchenStationName { get; set; } = string.Empty;
    public bool IsActive { get; set; }
}
