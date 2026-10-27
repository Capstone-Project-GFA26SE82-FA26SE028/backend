namespace Ordering.BLL.Interfaces;

public interface IBillingSessionClient
{
    Task<Guid> GetRestaurantIdAsync(Guid sessionId, CancellationToken ct);
}
