using Microsoft.EntityFrameworkCore;
using Ordering.DAL.Interfaces;
using Ordering.DAL.Models;

namespace Ordering.DAL.Implements;

public sealed class CancellationRequestRepository(OrderingDbContext db) : ICancellationRequestRepository
{
    public Task<CancellationRequest?> GetByIdAsync(Guid id, CancellationToken ct)
    {
        var query = db.Database.IsSqlServer()
            ? db.CancellationRequests.FromSqlInterpolated($"SELECT * FROM dbo.CancellationRequest WITH (UPDLOCK, HOLDLOCK) WHERE CancellationRequestId = {id}")
            : db.CancellationRequests.Where(item => item.CancellationRequestId == id);
        return query.Include(item => item.OrderItem).ThenInclude(item => item.Order)
            .SingleOrDefaultAsync(ct);
    }

    public Task<OrderItem?> GetItemAsync(Guid id, CancellationToken ct)
    {
        var query = db.Database.IsSqlServer()
            ? db.OrderItems.FromSqlInterpolated($"SELECT * FROM dbo.OrderItem WITH (UPDLOCK, HOLDLOCK) WHERE OrderItemId = {id}")
            : db.OrderItems.Where(item => item.OrderItemId == id);
        return query.Include(item => item.KitchenTasks).ThenInclude(task => task.Events)
            .Include(item => item.Order).ThenInclude(order => order.OrderItems)
            .SingleOrDefaultAsync(ct);
    }

    public Task<bool> HasPendingAsync(Guid itemId, CancellationToken ct)
    {
        return db.CancellationRequests.AnyAsync(item => item.OrderItemId == itemId && item.Status == "Pending", ct);
    }

    public async Task<(List<CancellationRequest> Items, int Total)> GetAllAsync(Guid? sessionId, string? status,
        int page, int pageSize, CancellationToken ct)
    {
        var query = db.CancellationRequests.AsNoTracking();
        if (sessionId.HasValue)
        {
            query = query.Where(item => item.OrderItem.Order.TableSessionRef == sessionId.Value);
        }

        if (status is not null)
        {
            query = query.Where(item => item.Status == status);
        }

        var total = await query.CountAsync(ct);
        var items = await query.Include(item => item.OrderItem).ThenInclude(item => item.Order)
            .OrderByDescending(item => item.RequestedAt).ThenBy(item => item.CancellationRequestId)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return (items, total);
    }

    public void Create(CancellationRequest request)
    {
        db.CancellationRequests.Add(request);
    }
}
