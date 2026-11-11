using Billing.Common.DTOs;
using Shared.Common.Pagination;

namespace Billing.BLL.Interfaces;

public interface IBillService
{
    Task<BillDTO> Create(Guid tableSessionId, CancellationToken ct);
    Task<BillDTO> CreateForCheckout(Guid tableSessionId, Guid checkoutId, CancellationToken ct);
    Task<BillDTO> Refresh(Guid id, CancellationToken ct);
    Task<BillDTO> RefreshForCheckout(Guid id, Guid checkoutId, CancellationToken ct);
    Task<PagedResult<BillDTO>> GetAll(BillQueryDTO query, CancellationToken ct);
    Task<BillDTO> GetPrebill(Guid sessionId, Guid checkoutId, CancellationToken ct);
    Task<BillDTO> GetById(Guid id, CancellationToken ct);
}
