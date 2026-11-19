using Ordering.Common.DTOs;
using Shared.Common.Pagination;

namespace Ordering.BLL.Interfaces;

public interface ICancellationRequestService
{
    Task<(CancellationRequestDTO Result, bool Created)> Create(Guid sessionId, Guid id, Guid itemId, int quantity, string reason, string guestToken, CancellationToken ct);
    Task<(CancellationRequestDTO Result, bool Created)> CancelByStaff(Guid sessionId, Guid id, Guid itemId, int quantity, string reason, CancellationToken ct);
    Task<CancellationRequestDTO> GetById(Guid sessionId, Guid id, string guestToken, CancellationToken ct);
    Task<PagedResult<CancellationRequestDTO>> GetAll(Guid? sessionId, string? status, int page, int pageSize, string guestToken, CancellationToken ct);
    Task<CancellationRequestDTO> Approve(Guid sessionId, Guid id, CancellationToken ct);
    Task<CancellationRequestDTO> Reject(Guid sessionId, Guid id, string reason, CancellationToken ct);
}
