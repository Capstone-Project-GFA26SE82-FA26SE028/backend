using System.ComponentModel.DataAnnotations;

namespace ApiGateway.DTOs.Inventory;

public sealed class IngredientWriteDTO
{
    [Required, StringLength(255)]
    public string IngredientName { get; set; } = "";
    [Required, StringLength(50)]
    public string BaseUnit { get; set; } = "";
    public decimal LowStockThreshold { get; set; }
}

public sealed class IngredientStatusDTO
{
    [Required]
    public bool? IsActive { get; set; }
}

public sealed class ReceiptLineDTO
{
    public Guid? UnitId { get; set; }
    public Guid IngredientId { get; set; }
    public decimal ReceivedQuantity { get; set; }
    [Required]
    public DateOnly? ExpiryDate { get; set; }
}

public sealed class ReceiptWriteDTO
{
    public Guid GoodsReceiptId { get; set; }
    [StringLength(1000)]
    public string? Note { get; set; }
    [Required, MinLength(1), MaxLength(100)]
    public List<ReceiptLineDTO> Items { get; set; } = [];
}
