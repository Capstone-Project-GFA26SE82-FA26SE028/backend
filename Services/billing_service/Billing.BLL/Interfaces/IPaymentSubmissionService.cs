using Billing.Common.DTOs;
using Shared.Common.Pagination;

namespace Billing.BLL.Interfaces;

public interface IPaymentSubmissionService
{
    Task<PaymentSubmissionDTO> Submit(Guid sessionId, Guid requestId, CancellationToken ct);
    Task<PagedResult<PaymentSubmissionDTO>> GetIncoming(Guid restaurantId, int page, int pageSize, CancellationToken ct);
}
