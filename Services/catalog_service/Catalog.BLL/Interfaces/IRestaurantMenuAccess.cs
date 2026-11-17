namespace Catalog.BLL.Interfaces;

public interface IRestaurantMenuAccess
{
    Task RequireManagerAsync(Guid restaurantId, CancellationToken ct);
}
