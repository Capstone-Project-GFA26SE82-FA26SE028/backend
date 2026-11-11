using Billing.Common.DTOs;
using Shared.Common.Pagination;

namespace Billing.BLL.Interfaces;

public interface IBillCheckoutService
{
    Task<BillCheckoutDTO> Request(Guid sessionId, Guid id, string guestToken, CancellationToken ct);
    Task<BillCheckoutDTO> GetCurrent(Guid sessionId, string guestToken, CancellationToken ct);
    Task<BillCheckoutDTO> Synchronize(Guid sessionId, Guid id, CancellationToken ct);
    Task<BillCheckoutDTO> Cancel(Guid sessionId, Guid id, CancellationToken ct);
    Task<BillCheckoutDTO> Submit(Guid sessionId, Guid id, Guid cashRegisterId, CancellationToken ct);
    Task<PagedResult<BillCheckoutDTO>> GetIncoming(Guid cashRegisterId, int page, int pageSize, CancellationToken ct);
    Task<BillCheckoutDTO> Present(Guid sessionId, Guid id, CancellationToken ct);
    Task AuthorizeRelease(Guid sessionId, Guid id, CancellationToken ct);
}
