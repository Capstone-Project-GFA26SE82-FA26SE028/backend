using System.ComponentModel.DataAnnotations;

namespace ApiGateway.DTOs.Inventory;

public sealed class UnitWriteDTO
{
    [Required, StringLength(50)]
    public string Code { get; set; } = "";
    [Required, StringLength(100)]
    public string Name { get; set; } = "";
}

public sealed class ConversionWriteDTO
{
    public decimal ConversionFactor { get; set; }
}
