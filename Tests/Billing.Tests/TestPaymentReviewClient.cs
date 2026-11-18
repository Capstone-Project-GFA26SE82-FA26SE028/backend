using Billing.BLL.Interfaces;

namespace Billing.Tests;

internal sealed class TestPaymentReviewClient : IPaymentReviewClient
{
    public Task<Guid> GetResultRestaurantAsync(Guid sessionId, Guid requestId, CancellationToken ct)
    {
        throw new InvalidOperationException("This test does not read payment results.");
    }

    public Task<(Guid RestaurantId, int PendingOrderCount)> GetReviewAsync(Guid sessionId, Guid requestId, CancellationToken ct)
    {
        throw new InvalidOperationException("This test only exercises legacy checkout.");
    }
}
