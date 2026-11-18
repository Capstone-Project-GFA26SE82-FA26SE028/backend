namespace Billing.BLL.Interfaces;

public interface IPaidSessionClosureClient
{
    Task TryCloseAsync(Guid restaurantId, Guid sessionId, CancellationToken ct);
}
