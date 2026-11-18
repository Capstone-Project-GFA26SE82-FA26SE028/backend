using System.Linq.Expressions;
using Billing.Common.DTOs;
using Billing.DAL.Models;
using Shared.Common.Pagination;

namespace Billing.DAL.Interfaces;

public interface IBillCheckoutRepository
{
    Task<BillCheckout?> GetByIdAsync(Guid id, CancellationToken ct);
    Task<BillCheckout?> GetActiveAsync(Guid sessionId, CancellationToken ct);
    Task<Guid?> GetBillIdAsync(Guid sessionId, CancellationToken ct);
    Task<bool> HasPaymentOrFinalBillAsync(Guid sessionId, CancellationToken ct, Expression<Func<Payment, bool>>? blocking = null);
    Task<PagedResult<PaymentSubmissionDTO>> GetIncomingAsync(Guid restaurantId, Guid cashRegisterId,
        int page, int pageSize, CancellationToken ct);
    void Add(BillCheckout value);
}
