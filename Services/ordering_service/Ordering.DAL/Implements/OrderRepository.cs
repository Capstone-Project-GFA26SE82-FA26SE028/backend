using Microsoft.EntityFrameworkCore;
using Ordering.DAL.Interfaces;
using Ordering.DAL.Models;
using Ordering.Common.DTOs;

namespace Ordering.DAL.Implements;

public sealed class OrderRepository(OrderingDbContext db) : IOrderRepository
{
    public Task<List<Order>> GetForTransferAsync(Guid sessionId, CancellationToken ct)
    {
        return Details(db.Orders.Where(order => order.TableSessionRef == sessionId))
            .OrderBy(order => order.OrderId).ToListAsync(ct);
    }
    public async Task<List<OrderReportSnapshotDTO>> GetReportItemsAsync(Guid restaurantId, Guid[] orderIds, CancellationToken ct)
    {
        // One SQL statement keeps order/item/cancellation snapshots consistent without a network-held transaction.
        var values = await db.Orders.AsNoTracking()
            .Where(value => value.RestaurantRef == restaurantId && orderIds.Contains(value.OrderId))
            .Include(value => value.OrderItems).ThenInclude(value => value.OrderItemOptions)
            .AsSingleQuery().ToListAsync(ct);
        return values.Select(order => new OrderReportSnapshotDTO(order.OrderId, order.Channel, order.Status,
            order.OrderItems.Select(item => new OrderReportItemDTO(item.OrderItemId, item.ItemRef,
                item.NameSnapshot, item.UnitPriceSnapshot, item.Quantity, item.CancelledQuantity, item.Status,
                item.OrderItemOptions.Select(option => new OrderReportOptionDTO(option.OptionRef,
                    option.NameSnapshot, option.Quantity, option.ExtraPriceSnapshot)).ToArray())).ToArray())).ToList();
    }

    public Task<List<OrderReportChannelDTO>> GetReportChannelsAsync(Guid restaurantId, Guid[] orderIds, CancellationToken ct)
    {
        return db.Orders.AsNoTracking()
            .Where(value => value.RestaurantRef == restaurantId && orderIds.Contains(value.OrderId))
            .Select(value => new OrderReportChannelDTO(value.OrderId, value.Channel)).ToListAsync(ct);
    }

    public Task<List<Order>> GetForBillingAsync(Guid sessionId, CancellationToken ct)
    {
        return db.Orders.AsNoTracking().Where(order => order.TableSessionRef == sessionId)
            .Include(order => order.OrderItems).ThenInclude(item => item.OrderItemOptions)
            .Include(order => order.OrderItems).ThenInclude(item => item.CancellationRequests)
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

    public Task<List<Order>> GetTakeawayAsync(Guid restaurantId, Guid? customerId, string? status, int page, int pageSize, CancellationToken ct)
    {
        var query = db.Orders.Where(order => order.RestaurantRef == restaurantId
            && order.Channel == "Takeaway" && order.TableSessionRef == null);
        if (customerId.HasValue)
        {
            query = query.Where(order => order.CustomerUserRef == customerId);
        }

        if (status is not null)
        {
            query = query.Where(order => order.Status == status);
        }

        return Details(query).OrderByDescending(order => order.CreatedAt).ThenBy(order => order.OrderId)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
    }

    public async Task<(List<Order> Items, int TotalCount)> GetCustomerTakeawayAsync(Guid customerId, string? status,
        int page, int pageSize, CancellationToken ct)
    {
        var query = db.Orders.IgnoreQueryFilters().AsNoTracking()
            .Where(order => order.CustomerUserRef == customerId && order.Channel == "Takeaway"
                && order.TableSessionRef == null);
        if (status is not null)
        {
            query = query.Where(order => order.Status == status);
        }

        var totalCount = await query.CountAsync(ct);
        var items = await query.OrderByDescending(order => order.CreatedAt).ThenBy(order => order.OrderId)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Include(order => order.OrderItems).ThenInclude(item => item.OrderItemOptions)
            .AsSingleQuery().ToListAsync(ct);
        return (items, totalCount);
    }

    private static IQueryable<Order> Details(IQueryable<Order> query)
    {
        return query
            .Include(order => order.OrderItems).ThenInclude(item => item.CancellationRequests)
            .Include(order => order.OrderItems)
                .ThenInclude(item => item.OrderItemOptions)
            .Include(order => order.OrderItems)
                .ThenInclude(item => item.KitchenTasks)
                    .ThenInclude(task => task.Events)
            .AsSplitQuery();
    }

    public Task<List<Order>> GetOnlinePendingAsync(CancellationToken ct)
    {
        return db.Orders.AsNoTracking().Where(order => order.CustomerUserRef != null
            && (order.Status == "PendingPayment" || (order.CancellationRequested && order.PaymentStatus != "Refunded"
                && order.PaymentStatus != "Cancelled")))
            .OrderBy(order => order.CreatedAt).Take(100).ToListAsync(ct);
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
