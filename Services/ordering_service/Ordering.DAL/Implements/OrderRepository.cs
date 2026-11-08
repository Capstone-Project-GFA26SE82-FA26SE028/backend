using Microsoft.EntityFrameworkCore;
using Ordering.DAL.Interfaces;
using Ordering.DAL.Models;

namespace Ordering.DAL.Implements;

public sealed class OrderRepository(OrderingDbContext db) : IOrderRepository
{
    public Task<List<Order>> GetForBillingAsync(Guid sessionId, CancellationToken ct)
    {
        return db.Orders.AsNoTracking().Where(order => order.TableSessionRef == sessionId)
            .Include(order => order.OrderItems).ThenInclude(item => item.OrderItemOptions)
            .OrderBy(order => order.OrderId).ToListAsync(ct);
    }

    public Task<Order?> GetByIdAsync(Guid id, CancellationToken ct)
    {
        var query = db.Database.IsSqlServer()
            ? db.Orders.FromSqlInterpolated($"SELECT * FROM [Order] WITH (UPDLOCK, HOLDLOCK) WHERE [OrderId] = {id}")
            : db.Orders.Where(order => order.OrderId == id);
        return Details(query).SingleOrDefaultAsync(ct);
    }

    public Task<Order?> GetByKeyAsync(string key, CancellationToken ct)
    {
        // Range/update locks protect the check-and-insert even without a unique index.
        var query = db.Database.IsSqlServer()
            ? db.Orders.FromSqlInterpolated($"SELECT * FROM [Order] WITH (UPDLOCK, HOLDLOCK) WHERE [IdempotencyKey] = {key}")
            : db.Orders.Where(order => order.IdempotencyKey == key);
        return Details(query).SingleOrDefaultAsync(ct);
    }

    public Task<List<Order>> GetAllAsync(Guid sessionId, string? guestScope, int page, int pageSize, CancellationToken ct)
    {
        var query = db.Orders.Where(order => order.TableSessionRef == sessionId);
        if (guestScope is not null)
        {
            query = query.Where(order => order.IdempotencyKey.StartsWith(guestScope));
        }

        return Details(query).OrderByDescending(order => order.CreatedAt).ThenBy(order => order.OrderId)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
    }

    private static IQueryable<Order> Details(IQueryable<Order> query)
    {
        return query
            .Include(order => order.OrderItems)
                .ThenInclude(item => item.OrderItemOptions)
            .Include(order => order.OrderItems)
                .ThenInclude(item => item.KitchenTasks)
                    .ThenInclude(task => task.Events)
            .AsSplitQuery();
    }

    public void Create(Order order)
    {
        db.Orders.Add(order);
    }

    public void SetOriginalVersion(Order order, byte[] version)
    {
        db.Entry(order).Property(value => value.Version).OriginalValue = version;
    }
}
