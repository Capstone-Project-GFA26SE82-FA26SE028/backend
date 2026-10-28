using System.Linq.Expressions;
using Billing.DAL.Models;

namespace Billing.DAL.Interfaces;

public interface IBillCheckoutRepository
{
    Task<BillCheckout?> GetByIdAsync(Guid id, CancellationToken ct);
    Task<BillCheckout?> GetActiveAsync(Guid sessionId, CancellationToken ct);
    Task<Guid?> GetBillIdAsync(Guid sessionId, CancellationToken ct);
    Task<bool> HasPaymentOrFinalBillAsync(Guid sessionId, CancellationToken ct, Expression<Func<Payment, bool>>? blocking = null);
    void Add(BillCheckout value);
}
