using System.Linq.Expressions;
using Billing.DAL.Models;

namespace Billing.DAL.Interfaces;

public interface IBillCheckoutRepository
{
    Task<BillCheckout?> GetByIdAsync(Guid id, CancellationToken ct);
    Task<BillCheckout?> GetActiveAsync(Guid sessionId, CancellationToken ct);
    Task<Guid?> GetBillIdAsync(Guid sessionId, CancellationToken ct);
    Task<bool> HasPaymentOrFinalBillAsync(Guid sessionId, CancellationToken ct, Expression<Func<Payment, bool>>? blocking = null);
    Task<IReadOnlyList<BillCheckout>> GetIncomingAsync(Guid cashRegisterId, int page, int pageSize, CancellationToken ct);
    Task<int> CountIncomingAsync(Guid cashRegisterId, CancellationToken ct);
    void Add(BillCheckout value);
}
