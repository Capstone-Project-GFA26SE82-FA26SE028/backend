using Microsoft.EntityFrameworkCore;
using Ordering.DAL.Interfaces;
using Ordering.DAL.Models;

namespace Ordering.DAL.Implements;

public sealed class KitchenStationRepository(OrderingDbContext db) : IKitchenStationRepository
{
    public async Task<(List<KitchenStation> Items, int Total)> GetPageAsync(
        int page, int pageSize, string? search, bool? isActive, CancellationToken ct)
    {
        var query = db.KitchenStations.AsNoTracking();
        if (isActive.HasValue)
        {
            query = query.Where(station => station.IsActive == isActive.Value);
        }
        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(station => station.KitchenStationCode.Contains(search)
                || station.KitchenStationName.Contains(search));
        }
        var total = await query.CountAsync(ct);
        var items = await query.OrderBy(station => station.KitchenStationCode)
            .ThenBy(station => station.KitchenStationId)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return (items, total);
    }

    public Task<KitchenStation?> GetByIdAsync(Guid id, CancellationToken ct)
    {
        return db.KitchenStations.SingleOrDefaultAsync(station => station.KitchenStationId == id, ct);
    }

    public Task<bool> CodeExistsAsync(string code, Guid excludedId, CancellationToken ct)
    {
        return db.KitchenStations.AnyAsync(station => station.KitchenStationId != excludedId
            && station.KitchenStationCode.ToUpper() == code, ct);
    }

    public async Task CreateAsync(KitchenStation station, CancellationToken ct)
    {
        await db.KitchenStations.AddAsync(station, ct);
    }
}
