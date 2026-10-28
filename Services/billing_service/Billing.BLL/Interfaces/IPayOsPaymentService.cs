using Billing.Common.DTOs;

namespace Billing.BLL.Interfaces;

public interface IPayOsPaymentService
{
    Task<(PayOsPaymentDTO Payment, bool Created)> Create(Guid billId, string key, CreatePayOsPaymentDTO request, CancellationToken ct);
    Task<PayOsPaymentDTO> GetById(Guid id, CancellationToken ct);
    Task<PayOsPaymentDTO> Reconcile(Guid id, CancellationToken ct);
    Task<PayOsPaymentDTO> Cancel(Guid id, CancellationToken ct);
    Task<PayOsPaymentDTO> Synchronize(Guid id, CancellationToken ct);
}
