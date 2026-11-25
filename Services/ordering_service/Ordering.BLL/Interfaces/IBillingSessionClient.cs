namespace Ordering.BLL.Interfaces;

public interface IBillingSessionClient
{
    Task<Guid> GetRestaurantIdAsync(Guid sessionId, CancellationToken ct);

    async Task<(Guid RestaurantId, Guid? CustomerRef)> GetSessionInfoAsync(Guid sessionId, CancellationToken ct)
    {
        var id = await GetRestaurantIdAsync(sessionId, ct);
        return (id, null);
    }
}
