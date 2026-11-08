namespace Restaurant.Common.DTOs;

public sealed class BillingSessionDto
{
    public Guid TableSessionId { get; set; }
    public Guid RestaurantId { get; set; }
}
