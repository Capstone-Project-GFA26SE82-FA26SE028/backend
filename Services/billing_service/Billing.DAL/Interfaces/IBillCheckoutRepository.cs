using System.Linq.Expressions;
using Billing.Common.DTOs;
using Billing.DAL.Models;

namespace Billing.DAL.Interfaces;

public interface IBillCheckoutRepository
{
    Task<BillCheckout?> GetByIdAsync(Guid id, CancellationToken ct);
    Task<BillCheckout?> GetActiveAsync(Guid sessionId, CancellationToken ct);
    Task<Guid?> GetBillIdAsync(Guid sessionId, CancellationToken ct);
    Task<bool> HasPaymentOrFinalBillAsync(Guid sessionId, CancellationToken ct, Expression<Func<Payment, bool>>? blocking = null);
    Task<IReadOnlyList<PaymentSubmissionDTO>> GetIncomingCandidatesAsync(Guid restaurantId, Guid cashRegisterId, CancellationToken ct);
    Task<IReadOnlyList<PaymentSubmissionDTO>> GetPaidCandidatesAsync(Guid restaurantId, Guid cashRegisterId,
        Guid cashShiftId, CancellationToken ct);
    void Add(BillCheckout value);
}
