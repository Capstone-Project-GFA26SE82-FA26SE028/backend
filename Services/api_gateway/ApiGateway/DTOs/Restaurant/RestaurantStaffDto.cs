namespace ApiGateway.DTOs.Restaurant;

public sealed class RestaurantStaffDto
{
    public Guid RestaurantId { get; set; }
    public Guid UserIdRef { get; set; }
    public bool IsActive { get; set; }
}
