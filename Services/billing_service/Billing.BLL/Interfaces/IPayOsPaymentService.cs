using Billing.Common.DTOs;
using Shared.Common.Pagination;

namespace Billing.BLL.Interfaces;

public interface IPayOsPaymentService
{
    Task<(PayOsPaymentDTO Payment, bool Created)> Create(Guid billId, string key, CreatePayOsPaymentDTO request, CancellationToken ct);
    Task<PagedResult<WaiterPayOsPaymentDTO>> GetForCheckout(Guid sessionId, Guid checkoutId, int page, int pageSize, CancellationToken ct);
    Task<WaiterPayOsPaymentDTO> GetForWaiter(Guid sessionId, Guid checkoutId, Guid paymentId, CancellationToken ct);
    Task<PayOsPaymentDTO> GetById(Guid id, CancellationToken ct);
    Task<PayOsPaymentDTO> Reconcile(Guid id, CancellationToken ct);
    Task<PayOsPaymentDTO> Cancel(Guid id, CancellationToken ct);
    Task<PayOsPaymentDTO> Synchronize(Guid id, CancellationToken ct);
}
