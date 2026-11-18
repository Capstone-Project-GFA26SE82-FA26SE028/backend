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

    public async Task<Guid?> GetClosingPaymentIdAsync(Guid sessionId, DateTime closedAt, CancellationToken ct)
    {
        var matches = await _context.ServiceRequests.AsNoTracking().Where(request =>
                request.TableSessionId == sessionId && request.Type == PaymentRequestStatuses.Type
                && request.Status == PaymentRequestStatuses.Completed && request.CompletedAt == closedAt
                && request.HandledByRef != null && request.AcknowledgedAt != null)
            .Select(request => request.ServiceRequestId).Take(2).ToListAsync(ct);
        return matches.Count == 1 ? matches[0] : null;
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

    public async Task<IReadOnlyList<(Guid RestaurantId, Guid SessionId)>> GetClosureCandidatesAsync(int offset, int size, CancellationToken ct)
    {
        var rows = await PendingPayments().AsNoTracking()
            .Where(request => request.Status == PaymentRequestStatuses.Acknowledged && request.HandledByRef != null
                && request.TableSession.Status == "Open" && request.TableSession.ClosedAt == null)
            .OrderBy(request => request.RequestedAt).ThenBy(request => request.ServiceRequestId)
            .Select(request => new
            {
                SessionId = request.TableSessionId,
                RestaurantId = request.TableSession.SessionTables.Where(link => link.ReleasedAt == null)
                    .OrderBy(link => link.TableId).Select(link => link.Table.Area.RestaurantId).FirstOrDefault()
            }).Skip(offset).Take(size).ToListAsync(ct);
        return rows.Select(value => (value.RestaurantId, value.SessionId)).ToArray();
    }

    private IQueryable<ServiceRequest> PendingPayments()
    {
        return _context.ServiceRequests.Where(request => request.Type == PaymentRequestStatuses.Type
            && request.CompletedAt == null);
    }
}
