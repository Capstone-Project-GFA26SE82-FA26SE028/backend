namespace Shared.Persistence.RestaurantIsolation;

public sealed class RestaurantDataScope
{
    public Guid? RestaurantId { get; private set; }

    public void Select(Guid restaurantId)
    {
        if (restaurantId == Guid.Empty || (RestaurantId.HasValue && RestaurantId != restaurantId))
        {
            throw new RestaurantScopeException("Không được thay đổi phạm vi nhà hàng trong cùng một thao tác.");
        }

        RestaurantId = restaurantId;
    }
}

public sealed class RestaurantScopeException(string message) : Exception(message)
{
}
