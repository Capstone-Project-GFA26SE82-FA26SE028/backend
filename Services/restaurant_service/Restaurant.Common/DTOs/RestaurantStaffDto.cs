namespace Restaurant.Common.DTOs;

public sealed class RestaurantStaffDto
{
    public Guid RestaurantId { get; set; }
    public Guid UserIdRef { get; set; }
    public bool IsActive { get; set; }
}
