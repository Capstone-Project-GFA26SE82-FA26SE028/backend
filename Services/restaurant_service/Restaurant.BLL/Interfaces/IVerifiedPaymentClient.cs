namespace Restaurant.BLL.Interfaces;

public interface IVerifiedPaymentClient
{
    Task<Guid?> GetCheckoutAsync(Guid restaurantId, Guid sessionId, CancellationToken ct);
}
