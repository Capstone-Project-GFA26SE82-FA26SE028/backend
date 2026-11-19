using Restaurant.DAL.Models;
using Shared.Persistence.Interfaces;

namespace Restaurant.DAL.Interfaces;

public interface IServiceRequestRepository : IGenericRepository<ServiceRequest>
{
    Task<ServiceRequest?> GetPendingAsync(Guid sessionId, string type, CancellationToken ct);
    Task<(IReadOnlyList<ServiceRequest> Items, int Total)> GetPageAsync(Guid restaurantId, string type, int page, int pageSize, CancellationToken ct);
    Task<bool> HasUnfinishedForStaffAsync(Guid userId, CancellationToken ct);
    Task CancelPendingStaffCallsAsync(Guid sessionId, DateTime closedAt, CancellationToken ct);
    Task EnqueueCreatedNotificationAsync(ServiceRequest request, Guid restaurantId, CancellationToken ct);
    Task<IReadOnlyList<(Guid RestaurantId, Guid SessionId)>> GetClosureCandidatesAsync(int offset, int size, CancellationToken ct);
    Task<ServiceRequest?> GetPendingPaymentAsync(Guid sessionId, CancellationToken ct);
    Task<Guid?> GetClosingPaymentIdAsync(Guid sessionId, DateTime closedAt, CancellationToken ct);
    Task<bool> HasPendingPaymentAsync(Guid sessionId, CancellationToken ct);
    Task<(IReadOnlyList<ServiceRequest> Items, int Total)> GetPaymentPageAsync(
        Guid restaurantId, int page, int pageSize, CancellationToken ct);
}
