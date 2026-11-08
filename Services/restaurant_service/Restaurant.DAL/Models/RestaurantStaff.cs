namespace Restaurant.DAL.Models;

public partial class RestaurantStaff
{
    public Guid RestaurantId { get; set; }
    public Guid UserIdRef { get; set; }
    public bool IsActive { get; set; }
    public virtual Restaurant Restaurant { get; set; } = null!;
}
