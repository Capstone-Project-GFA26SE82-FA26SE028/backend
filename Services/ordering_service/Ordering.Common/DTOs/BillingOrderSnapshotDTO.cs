namespace Ordering.Common.DTOs;

public sealed class BillingOrderSnapshotDTO
{
    public Guid TableSessionId { get; set; }
    public Guid RestaurantId { get; set; }
    public int PendingOrderCount { get; set; }
    public List<BillingOrderLineDTO> Items { get; set; } = [];
}

public sealed class BillingOrderLineDTO
{
    public Guid OrderId { get; set; }
    public Guid OrderItemId { get; set; }
    public Guid ItemId { get; set; }
    public Guid OriginalTableId { get; set; }
    public Guid? ComboGroupId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public string Status { get; set; } = string.Empty;
    public List<QuotedOptionDTO> Options { get; set; } = [];
}
