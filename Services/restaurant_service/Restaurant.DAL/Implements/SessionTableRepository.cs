using Microsoft.EntityFrameworkCore;
using Restaurant.DAL.Interfaces;
using Restaurant.DAL.Models;
using Shared.Persistence.Repositories;

namespace Restaurant.DAL.Implements;

public sealed class SessionTableRepository(RestaurantDbContext db)
    : GenericRepository<SessionTable, RestaurantDbContext>(db), ISessionTableRepository
{
    public async Task<IReadOnlyList<SessionTable>> GetUnreleasedForTablesAsync(IReadOnlyCollection<Guid> tableIds, CancellationToken ct)
    {
        return await _context.SessionTables.AsNoTracking().Include(link => link.TableSession)
            .Where(link => link.ReleasedAt == null && link.TableSession.ClosedAt == null && tableIds.Contains(link.TableId)).ToListAsync(ct);
    }
}
