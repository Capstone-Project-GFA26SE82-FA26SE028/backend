namespace Inventory.Common.DTOs;

public sealed class StockIssueLineDTO
{
    public Guid IngredientId { get; set; }
    public decimal Quantity { get; set; }
}

public sealed class StockIssueWriteDTO
{
    public Guid StockIssueId { get; set; }
    public string Reason { get; set; } = "";
    public List<StockIssueLineDTO> Items { get; set; } = [];
}

public sealed class StockLotDTO
{
    public Guid GoodsReceiptItemId { get; set; }
    public Guid IngredientId { get; set; }
    public DateTime ReceivedAt { get; set; }
    public DateOnly? ExpiryDate { get; set; }
    public decimal ReceivedQuantity { get; set; }
    public decimal RemainingQuantity { get; set; }
    public string ExpiryStatus { get; set; } = "";
    public bool CanIssue { get; set; }
}

public sealed class CookingItemDeductionDTO
{
    public Guid TaskId { get; set; }
    public Guid OrderItemId { get; set; }
    public string DishName { get; set; } = "";
    public int Quantity { get; set; }
    public List<StockIssueLineDTO> Ingredients { get; set; } = [];
}

public sealed class CookingDeductionDTO
{
    public Guid CookingEventId { get; set; }
    public List<CookingItemDeductionDTO> Items { get; set; } = [];
}

public sealed class LowStockAlertDTO
{
    public Guid IngredientId { get; set; }
    public string IngredientName { get; set; } = "";
    public decimal CurrentStock { get; set; }
    public decimal Threshold { get; set; }
}

public sealed class CookingMovementDTO
{
    public Guid StockMovementId { get; set; }
    public Guid? StockIssueId { get; set; }
    public Guid IngredientId { get; set; }
    public Guid? GoodsReceiptItemId { get; set; }
    public string MovementType { get; set; } = "";
    public decimal QuantityChange { get; set; }
    public Guid? CookingEventRef { get; set; }
    public Guid? OrderItemRef { get; set; }
    public Guid? PerformedByRef { get; set; }
    public string Reason { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}

public sealed class CookingDeductionResultDTO
{
    public Guid StockIssueId { get; set; }
    public List<CookingMovementDTO> Movements { get; set; } = [];
    public List<LowStockAlertDTO> LowStockAlerts { get; set; } = [];
}
