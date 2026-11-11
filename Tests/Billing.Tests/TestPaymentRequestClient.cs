using Billing.BLL.Interfaces;
using Billing.Common.Exceptions;

namespace Billing.Tests;

internal sealed class TestPaymentRequestClient : IPaymentRequestClient
{
    public bool Reviewed { get; set; } = true;
    public bool FailCreate { get; set; }
    public bool FailCancel { get; set; }
    public HashSet<Guid> Requests { get; } = [];
    public HashSet<Guid> Cancelled { get; } = [];

    public Task CreateAsync(Guid sessionId, Guid checkoutId, string guestToken, CancellationToken ct)
    {
        Requests.Add(checkoutId);
        if (FailCreate)
        {
            throw new BillingException(503, "Payment request reply lost.");
        }

        return Task.CompletedTask;
    }

    public Task RequireReviewedAsync(Guid sessionId, Guid checkoutId, CancellationToken ct)
    {
        if (!Reviewed)
        {
            throw new BillingException(409, "Waiter review is required.");
        }

        return Task.CompletedTask;
    }

    public Task CancelAsync(Guid sessionId, Guid checkoutId, CancellationToken ct)
    {
        if (FailCancel)
        {
            throw new BillingException(503, "Payment request cancellation reply lost.");
        }

        Cancelled.Add(checkoutId);
        return Task.CompletedTask;
    }
}
