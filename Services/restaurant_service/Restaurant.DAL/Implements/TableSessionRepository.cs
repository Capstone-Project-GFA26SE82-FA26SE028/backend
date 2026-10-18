using Microsoft.EntityFrameworkCore;
using Restaurant.DAL.Interfaces;
using Restaurant.DAL.Models;
using Shared.Persistence.Repositories;

namespace Restaurant.DAL.Implements;

public sealed class TableSessionRepository(RestaurantDbContext db)
    : GenericRepository<TableSession, RestaurantDbContext>(db), ITableSessionRepository
{
    public Task<List<TableSession>> GetOpenForTableAsync(Guid tableId, CancellationToken ct)
    {
        return _context.TableSessions.Where(x => x.ClosedAt == null
            && x.SessionTables.Any(link => link.TableId == tableId && link.ReleasedAt == null))
            .Take(2).ToListAsync(ct);
    }
}
