namespace ApiGateway.DTOs.Ordering;

public sealed class MyTakeawayOrderDTO
{
    public Guid OrderId { get; set; }
    public Guid? RestaurantId { get; set; }
    public string? RestaurantName { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? PaymentStatus { get; set; }
    public decimal TotalAmount { get; set; }
    public List<MyTakeawayOrderItemDTO> Items { get; set; } = [];
}

public sealed class MyTakeawayOrderItemDTO : OrderLineDTO
{
    public string? ImageUrl { get; set; }
}
