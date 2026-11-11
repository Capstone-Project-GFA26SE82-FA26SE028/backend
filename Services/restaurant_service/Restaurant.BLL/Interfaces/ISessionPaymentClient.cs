namespace Restaurant.BLL.Interfaces;

public interface ISessionPaymentClient
{
    Task RequirePaidAsync(Guid sessionId, Guid restaurantId, CancellationToken ct);
}
