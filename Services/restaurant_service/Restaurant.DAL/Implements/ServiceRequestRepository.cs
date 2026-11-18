using Microsoft.EntityFrameworkCore;
using Restaurant.Common.Constants;
using Restaurant.DAL.Interfaces;
using Restaurant.DAL.Models;
using Shared.Persistence.Repositories;

namespace Restaurant.DAL.Implements;

public sealed class ServiceRequestRepository(RestaurantDbContext db)
    : GenericRepository<ServiceRequest, RestaurantDbContext>(db), IServiceRequestRepository
{
    public Task<ServiceRequest?> GetPendingPaymentAsync(Guid sessionId, CancellationToken ct)
    {
        return PendingPayments().AsNoTracking().SingleOrDefaultAsync(request => request.TableSessionId == sessionId, ct);
    }

    public Task<bool> HasPendingPaymentAsync(Guid sessionId, CancellationToken ct)
    {
        return PendingPayments().AnyAsync(request => request.TableSessionId == sessionId, ct);
    }

    public async Task<(IReadOnlyList<ServiceRequest> Items, int Total)> GetPaymentPageAsync(
        Guid restaurantId, int page, int pageSize, CancellationToken ct)
    {
        var query = PendingPayments().AsNoTracking().Where(request =>
            request.TableSession.Status == "Open" && request.TableSession.ClosedAt == null
            && request.TableSession.SessionTables.Any(link => link.ReleasedAt == null && link.Table.Area.RestaurantId == restaurantId)
            && !request.TableSession.SessionTables.Any(link => link.ReleasedAt == null && link.Table.Area.RestaurantId != restaurantId));

        var total = await query.CountAsync(ct);
        var items = await query.OrderBy(request => request.RequestedAt).ThenBy(request => request.ServiceRequestId)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Include(request => request.TableSession)
                .ThenInclude(session => session.SessionTables.Where(link => link.ReleasedAt == null))
                .ThenInclude(link => link.Table).ThenInclude(table => table.Area)
            .ToListAsync(ct);
        return (items, total);
    }

    private IQueryable<ServiceRequest> PendingPayments()
    {
        return _context.ServiceRequests.Where(request => request.Type == PaymentRequestStatuses.Type
            && request.CompletedAt == null);
    }
}
