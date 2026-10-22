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
