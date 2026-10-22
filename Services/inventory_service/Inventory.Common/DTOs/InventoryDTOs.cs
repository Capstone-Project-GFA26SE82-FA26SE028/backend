namespace Inventory.Common.DTOs;

public sealed class IngredientWriteDTO
{
    public string IngredientName { get; set; } = "";
    public string BaseUnit { get; set; } = "";
    public decimal LowStockThreshold { get; set; }
}

public sealed class ReceiptLineDTO
{
    public Guid? UnitId { get; set; }
    public Guid IngredientId { get; set; }
    public decimal ReceivedQuantity { get; set; }
    public DateOnly? ExpiryDate { get; set; }
}

public sealed class ReceiptWriteDTO
{
    public Guid GoodsReceiptId { get; set; }
    public string? Note { get; set; }
    public List<ReceiptLineDTO> Items { get; set; } = [];
}

public sealed class PageDTO<T>
{
    public List<T> Items { get; set; } = [];
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
}
