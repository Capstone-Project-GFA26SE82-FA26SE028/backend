using Billing.DAL.Models;
using Shared.Persistence.Interfaces;

namespace Billing.DAL.Interfaces;

public interface IPaymentWebhookRepository : IGenericRepository<PaymentWebhook>
{
    Task<bool> LockKeyAsync(string key, CancellationToken ct);
    Task<IReadOnlyList<PaymentWebhook>> FindAsync(string key, CancellationToken ct);
    Task<IReadOnlyList<PaymentWebhook>> PendingAsync(Guid paymentId, CancellationToken ct);
}
