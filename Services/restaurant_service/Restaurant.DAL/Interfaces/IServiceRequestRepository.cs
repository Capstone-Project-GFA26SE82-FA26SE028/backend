using Restaurant.DAL.Models;
using Shared.Persistence.Interfaces;

namespace Restaurant.DAL.Interfaces;

public interface IServiceRequestRepository : IGenericRepository<ServiceRequest>
{
    Task<ServiceRequest?> GetPendingPaymentAsync(Guid sessionId, CancellationToken ct);
    Task<bool> HasPendingPaymentAsync(Guid sessionId, CancellationToken ct);
    Task<(IReadOnlyList<ServiceRequest> Items, int Total)> GetPaymentPageAsync(
        Guid restaurantId, int page, int pageSize, CancellationToken ct);
}
