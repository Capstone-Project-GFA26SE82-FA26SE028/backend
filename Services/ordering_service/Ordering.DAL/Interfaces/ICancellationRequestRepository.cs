using Ordering.DAL.Models;

namespace Ordering.DAL.Interfaces;

public interface ICancellationRequestRepository
{
    Task<CancellationRequest?> GetByIdAsync(Guid id, CancellationToken ct);
    Task<OrderItem?> GetItemAsync(Guid id, CancellationToken ct);
    Task<bool> HasPendingAsync(Guid itemId, CancellationToken ct);
    Task<(List<CancellationRequest> Items, int Total)> GetAllAsync(Guid? sessionId, string? status, int page, int pageSize, CancellationToken ct);
    void Create(CancellationRequest request);
}
