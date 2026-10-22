using Microsoft.EntityFrameworkCore;
using Ordering.DAL.Interfaces;
using Ordering.DAL.Models;

namespace Ordering.DAL.Implements;

public sealed class KitchenTaskRepository(OrderingDbContext db) : IKitchenTaskRepository
{
    public async Task<Guid?> GetOnlyActiveStationAsync(CancellationToken ct)
    {
        var ids = await db.KitchenStations.Where(station => station.IsActive)
            .Select(station => station.KitchenStationId).Take(2).ToListAsync(ct);
        return ids.Count == 1 ? ids[0] : null;
    }

    public Task<bool> IsStationActiveAsync(Guid stationId, CancellationToken ct)
    {
        return db.KitchenStations.AnyAsync(station => station.KitchenStationId == stationId && station.IsActive, ct);
    }

    public void Create(KitchenTask task)
    {
        db.KitchenTasks.Add(task);
    }
}
