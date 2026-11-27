namespace Catalog.BLL.Interfaces;

public interface IMenuAccess
{
    Task RequireOnlineReadAsync(Guid restaurantId, CancellationToken cancellationToken)
    {
        return RequireReadAsync(null, null, cancellationToken);
    }

    Task RequireTakeawayValidationAsync(Guid restaurantId, CancellationToken cancellationToken);

    Task RequireReadAsync(Guid? tableSessionId, string? guestToken, CancellationToken cancellationToken);
}
