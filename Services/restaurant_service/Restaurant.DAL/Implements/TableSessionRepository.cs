using Microsoft.EntityFrameworkCore;
using Restaurant.DAL.Interfaces;
using Restaurant.DAL.Models;
using Shared.Persistence.Repositories;

namespace Restaurant.DAL.Implements;

public sealed class TableSessionRepository(RestaurantDbContext db)
    : GenericRepository<TableSession, RestaurantDbContext>(db), ITableSessionRepository
{
    public Task<TableSession?> GetForOrderingAsync(Guid id, CancellationToken ct)
    {
        return _context.TableSessions.AsNoTracking()
            .Include(session => session.SessionTables.Where(link => link.ReleasedAt == null))
                .ThenInclude(link => link.Table).ThenInclude(table => table.Area)
            .SingleOrDefaultAsync(session => session.TableSessionId == id, ct);
    }

    public Task<TableSession?> LockAsync(Guid id, CancellationToken ct)
    {
        if (_context.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("Session lock requires a transaction.");
        }

        if (_context.Database.IsSqlServer())
        {
            return _context.TableSessions.FromSqlInterpolated(
                $"SELECT * FROM [TableSession] WITH (UPDLOCK, HOLDLOCK) WHERE [TableSessionId] = {id}")
                .SingleOrDefaultAsync(ct);
        }

        return GetByIdAsync(id, ct);
    }

    public Task<List<TableSession>> GetOpenForTableAsync(Guid tableId, CancellationToken ct)
    {
        return _context.TableSessions.Where(x => x.ClosedAt == null
            && x.SessionTables.Any(link => link.TableId == tableId && link.ReleasedAt == null))
            .Take(2).ToListAsync(ct);
    }
}
