namespace Billing.Common.DTOs;

public sealed class BillSourceDTO
{
    public Guid TableSessionId { get; set; }
    public Guid RestaurantId { get; set; }
    public int PendingOrderCount { get; set; }
    public List<BillSourceLineDTO> Items { get; set; } = [];
}

public sealed class BillSourceLineDTO
{
    public Guid OrderItemId { get; set; }
    public Guid OriginalTableId { get; set; }
    public Guid? ComboGroupId { get; set; }
    public int Quantity { get; set; }
    public BillItemSnapshot Item { get; set; } = new();
}
