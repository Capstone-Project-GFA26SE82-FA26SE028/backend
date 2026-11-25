using Billing.Common.DTOs;

namespace Billing.BLL.Interfaces;

public interface IPaymentSubmissionService
{
    Task<PaymentSubmissionDTO> Submit(Guid sessionId, Guid requestId, CancellationToken ct);
    Task<PaymentSubmissionPageDTO> GetIncoming(Guid restaurantId, PaymentSubmissionQueryDTO query, CancellationToken ct);
    Task<PaymentSubmissionPageDTO> GetPaid(Guid restaurantId, PaymentSubmissionQueryDTO query, CancellationToken ct);
}
