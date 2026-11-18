using Restaurant.DAL.Models;
using Shared.Persistence.Interfaces;

namespace Restaurant.DAL.Interfaces;

public interface IServiceRequestRepository : IGenericRepository<ServiceRequest>
{
    Task<IReadOnlyList<(Guid RestaurantId, Guid SessionId)>> GetClosureCandidatesAsync(int offset, int size, CancellationToken ct);
    Task<ServiceRequest?> GetPendingPaymentAsync(Guid sessionId, CancellationToken ct);
    Task<Guid?> GetClosingPaymentIdAsync(Guid sessionId, DateTime closedAt, CancellationToken ct);
    Task<bool> HasPendingPaymentAsync(Guid sessionId, CancellationToken ct);
    Task<(IReadOnlyList<ServiceRequest> Items, int Total)> GetPaymentPageAsync(
        Guid restaurantId, int page, int pageSize, CancellationToken ct);
}
