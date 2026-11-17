namespace Restaurant.BLL.Interfaces;

public interface IRestaurantDataScopeService
{
    Task<Guid> Resolve(Guid? restaurantId, Guid? sessionId, string guestToken, bool allowCustomer, CancellationToken ct);
}
