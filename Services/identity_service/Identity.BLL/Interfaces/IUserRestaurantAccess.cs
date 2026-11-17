namespace Identity.BLL.Interfaces;

public interface IUserRestaurantAccess
{
    Task<bool> CanManageAsync(Guid actorId, Guid restaurantId, CancellationToken ct);
}
