namespace Billing.BLL.Interfaces;

public interface IPaymentRequestClient
{
    Task CreateAsync(Guid sessionId, Guid checkoutId, string guestToken, CancellationToken ct);
    Task RequireReviewedAsync(Guid sessionId, Guid checkoutId, CancellationToken ct);
    Task CancelAsync(Guid sessionId, Guid checkoutId, CancellationToken ct);
}
