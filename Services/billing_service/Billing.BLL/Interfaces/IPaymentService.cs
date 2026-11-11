using Billing.Common.DTOs;

namespace Billing.BLL.Interfaces;

public interface IPaymentService
{
    Task RequirePaid(Guid sessionId, Guid restaurantId, CancellationToken ct);

    Task<(PaymentDTO Payment, bool Created)> PayCash(Guid billId, string key, CreateCashPaymentDTO request, CancellationToken ct);
    Task<PaymentDTO> GetById(Guid id, CancellationToken ct);
}
