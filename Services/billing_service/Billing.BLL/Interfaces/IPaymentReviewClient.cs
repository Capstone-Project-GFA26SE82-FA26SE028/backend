namespace Billing.BLL.Interfaces;

public interface IPaymentReviewClient
{
    Task<(Guid RestaurantId, int PendingOrderCount)> GetReviewAsync(Guid sessionId, Guid requestId, CancellationToken ct);
}
