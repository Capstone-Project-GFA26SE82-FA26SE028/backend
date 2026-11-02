using System.ComponentModel.DataAnnotations;

namespace ApiGateway.DTOs.Inventory;

public sealed class UnitWriteDTO
{
    [Required(ErrorMessage = "Trường {0} là bắt buộc."), StringLength(50, ErrorMessage = "Trường {0} phải có độ dài từ {2} đến {1} ký tự.")]
    public string Code { get; set; } = "";
    [Required(ErrorMessage = "Trường {0} là bắt buộc."), StringLength(100, ErrorMessage = "Trường {0} phải có độ dài từ {2} đến {1} ký tự.")]
    public string Name { get; set; } = "";
}

public sealed class ConversionWriteDTO
{
    public decimal ConversionFactor { get; set; }
}
