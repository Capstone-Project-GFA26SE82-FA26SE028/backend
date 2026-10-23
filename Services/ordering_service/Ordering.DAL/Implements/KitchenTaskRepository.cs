using Microsoft.EntityFrameworkCore;
using Ordering.DAL.Interfaces;
using Ordering.DAL.Models;

namespace Ordering.DAL.Implements;

public sealed class KitchenTaskRepository(OrderingDbContext db) : IKitchenTaskRepository
{
    public async Task<Guid?> GetOnlyActiveStationAsync(CancellationToken ct)
    {
        var ids = await db.KitchenStations
            .Where(station => station.IsActive)
            .Select(station => station.KitchenStationId)
            .Take(2)
            .ToListAsync(ct);

        return ids.Count == 1 ? ids[0] : null;
    }

    public Task<bool> IsStationActiveAsync(Guid stationId, CancellationToken ct)
    {
        return db.KitchenStations.AnyAsync(
            station => station.KitchenStationId == stationId && station.IsActive,
            ct);
    }

    public void Create(KitchenTask task)
    {
        db.KitchenTasks.Add(task);
    }

    public async Task<(List<KitchenTask> Items, int Total)> GetPageAsync(
        int page,
        int pageSize,
        Guid? stationId,
        string? status,
        Guid? orderId,
        Guid? sessionId,
        CancellationToken ct)
    {
        var query = db.KitchenTasks.AsNoTracking();

        if (stationId.HasValue && stationId.Value != Guid.Empty)
        {
            query = query.Where(t => t.KitchenStationId == stationId.Value);
        }

        if (orderId.HasValue && orderId.Value != Guid.Empty)
        {
            query = query.Where(t => t.OrderItem.OrderId == orderId.Value);
        }

        if (sessionId.HasValue && sessionId.Value != Guid.Empty)
        {
            query = query.Where(t => t.OrderItem.Order.TableSessionRef == sessionId.Value);
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            if (string.Equals(status, "active", StringComparison.OrdinalIgnoreCase))
            {
                query = query.Where(t => (t.Status == "Pending" || t.Status == "Cooking") && t.OrderItem.Status != "Served");
            }
            else if (string.Equals(status, "Ready", StringComparison.OrdinalIgnoreCase))
            {
                query = query.Where(t => t.Status == "Ready" && t.OrderItem.Status != "Served");
            }
            else if (!string.Equals(status, "all", StringComparison.OrdinalIgnoreCase))
            {
                query = query.Where(t => t.Status == status);
            }
        }
        else
        {
            query = query.Where(t => (t.Status == "Pending" || t.Status == "Cooking") && t.OrderItem.Status != "Served");
        }

        var total = await query.CountAsync(ct);

        var items = await query
            .Include(t => t.KitchenStation)
            .Include(t => t.OrderItem)
                .ThenInclude(i => i.OrderItemOptions)
            .Include(t => t.OrderItem)
                .ThenInclude(i => i.Order)
            .OrderByDescending(t => t.Priority)
            .ThenBy(t => t.QueuedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (items, total);
    }

    public Task<KitchenTask?> GetByIdAsync(Guid id, CancellationToken ct)
    {
        return db.KitchenTasks.AsNoTracking()
            .Include(t => t.KitchenStation)
            .Include(t => t.OrderItem)
                .ThenInclude(i => i.OrderItemOptions)
            .Include(t => t.OrderItem)
                .ThenInclude(i => i.Order)
            .FirstOrDefaultAsync(t => t.KitchenTaskId == id, ct);
    }

    public Task<KitchenTask?> GetByIdForUpdateAsync(Guid id, CancellationToken ct)
    {
        return db.KitchenTasks
            .Include(t => t.Events)
            .Include(t => t.KitchenStation)
            .Include(t => t.OrderItem)
                .ThenInclude(i => i.OrderItemOptions)
            .Include(t => t.OrderItem)
                .ThenInclude(i => i.Order)
                    .ThenInclude(o => o.OrderItems)
            .FirstOrDefaultAsync(t => t.KitchenTaskId == id, ct);
    }

    public async Task<(int Pending, int Cooking, int Ready, double AvgCookingMinutes)> GetSummaryAsync(
        Guid? stationId,
        CancellationToken ct)
    {
        var query = db.KitchenTasks.AsNoTracking();

        if (stationId.HasValue && stationId.Value != Guid.Empty)
        {
            query = query.Where(t => t.KitchenStationId == stationId.Value);
        }

        var pending = await query.CountAsync(t => t.Status == "Pending", ct);
        var cooking = await query.CountAsync(t => t.Status == "Cooking", ct);
        var ready = await query.CountAsync(t => t.Status == "Ready" && t.OrderItem.Status != "Served", ct);

        var completedTasks = await query
            .Where(t => t.CookingAt != null && t.ReadyAt != null)
            .OrderByDescending(t => t.ReadyAt)
            .Select(t => new { t.CookingAt, t.ReadyAt })
            .Take(100)
            .ToListAsync(ct);

        var avgMinutes = completedTasks.Count > 0
            ? completedTasks.Average(t => (t.ReadyAt!.Value - t.CookingAt!.Value).TotalMinutes)
            : 0.0;

        return (pending, cooking, ready, Math.Round(avgMinutes, 1));
    }
}
