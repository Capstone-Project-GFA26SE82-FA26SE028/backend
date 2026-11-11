using Microsoft.EntityFrameworkCore;
using Restaurant.DAL.Interfaces;
using Restaurant.DAL.Models;
using Shared.Persistence.Repositories;

namespace Restaurant.DAL.Implements;

public sealed class ServiceRequestRepository(RestaurantDbContext db)
    : GenericRepository<ServiceRequest, RestaurantDbContext>(db), IServiceRequestRepository
{
    public Task<ServiceRequest?> GetPaymentRequestAsync(Guid id, CancellationToken ct)
    {
        return _context.ServiceRequests.SingleOrDefaultAsync(request => request.ServiceRequestId == id
            && request.Type == "Payment", ct);
    }

    public Task<ServiceRequest?> GetActivePaymentRequestAsync(Guid sessionId, CancellationToken ct)
    {
        return _context.ServiceRequests.SingleOrDefaultAsync(request => request.TableSessionId == sessionId
            && request.Type == "Payment" && request.Status != "Cancelled", ct);
    }

    public async Task<(IReadOnlyList<ServiceRequest> Items, int Total)> GetPageAsync(Guid restaurantId,
        string? status, int page, int pageSize, CancellationToken ct)
    {
        var query = _context.ServiceRequests.AsNoTracking().Where(request => request.Type == "Payment"
            && request.TableSession.SessionTables.Any(link => link.ReleasedAt == null && link.Table.Area.RestaurantId == restaurantId)
            && !request.TableSession.SessionTables.Any(link => link.ReleasedAt == null && link.Table.Area.RestaurantId != restaurantId));
        if (status is not null)
        {
            query = query.Where(request => request.Status == status);
        }

        var total = await query.CountAsync(ct);
        var items = await query.OrderBy(request => request.RequestedAt).ThenBy(request => request.ServiceRequestId)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return (items, total);
    }
}
