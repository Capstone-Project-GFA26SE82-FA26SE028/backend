namespace Restaurant.BLL.Interfaces;

public interface ILayoutManagementAccess
{
    Task RequireManager(CancellationToken ct);

    Task<Guid> RequireRestaurant(Guid? restaurantId, CancellationToken ct);
}
