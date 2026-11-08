using System.ComponentModel.DataAnnotations;

namespace ApiGateway.DTOs.Inventory;

public sealed class IngredientWriteDTO
{
    [Required(ErrorMessage = "Trường {0} là bắt buộc."), StringLength(255, ErrorMessage = "Trường {0} phải có độ dài từ {2} đến {1} ký tự.")]
    public string IngredientName { get; set; } = "";
    [Required(ErrorMessage = "Trường {0} là bắt buộc."), StringLength(50, ErrorMessage = "Trường {0} phải có độ dài từ {2} đến {1} ký tự.")]
    public string BaseUnit { get; set; } = "";
    public decimal LowStockThreshold { get; set; }
}

public sealed class IngredientStatusDTO
{
    [Required(ErrorMessage = "Trường {0} là bắt buộc.")]
    public bool? IsActive { get; set; }
}

public sealed class ReceiptLineDTO
{
    public Guid? UnitId { get; set; }
    public Guid IngredientId { get; set; }
    public decimal ReceivedQuantity { get; set; }
    [Required(ErrorMessage = "Trường {0} là bắt buộc.")]
    public DateOnly? ExpiryDate { get; set; }
}

public sealed class ReceiptWriteDTO
{
    public Guid GoodsReceiptId { get; set; }
    [StringLength(1000, ErrorMessage = "Trường {0} phải có độ dài từ {2} đến {1} ký tự.")]
    public string? Note { get; set; }
    [Required(ErrorMessage = "Trường {0} là bắt buộc."), MinLength(1, ErrorMessage = "Trường {0} phải có ít nhất {1} phần tử hoặc ký tự."), MaxLength(100, ErrorMessage = "Trường {0} chỉ được có tối đa {1} phần tử hoặc ký tự.")]
    public List<ReceiptLineDTO> Items { get; set; } = [];
}
