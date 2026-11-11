using Restaurant.DAL.Models;
using Shared.Persistence.Interfaces;

namespace Restaurant.DAL.Interfaces;

public interface IServiceRequestRepository : IGenericRepository<ServiceRequest>
{
    Task<ServiceRequest?> GetPaymentRequestAsync(Guid id, CancellationToken ct);
    Task<ServiceRequest?> GetActivePaymentRequestAsync(Guid sessionId, CancellationToken ct);
    Task<(IReadOnlyList<ServiceRequest> Items, int Total)> GetPageAsync(Guid restaurantId, string? status, int page, int pageSize, CancellationToken ct);
}
