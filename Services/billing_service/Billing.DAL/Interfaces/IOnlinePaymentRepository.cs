using Billing.DAL.Models;

namespace Billing.DAL.Interfaces;

public interface IOnlinePaymentRepository
{
    Task<OnlinePayment?> GetByIdAsync(Guid id, CancellationToken ct);
    Task<OnlinePayment?> GetByCodeAsync(long code, CancellationToken ct);
    Task<List<Guid>> GetPendingAsync(CancellationToken ct);
    void Create(OnlinePayment payment);
}
