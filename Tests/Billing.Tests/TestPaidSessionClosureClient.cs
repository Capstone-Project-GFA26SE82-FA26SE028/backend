using Billing.BLL.Interfaces;

namespace Billing.Tests;

internal sealed class TestPaidSessionClosureClient : IPaidSessionClosureClient
{
    public Task TryCloseAsync(Guid restaurantId, Guid sessionId, CancellationToken ct)
    {
        return Task.CompletedTask;
    }
}
