using Microsoft.EntityFrameworkCore;
using Restaurant.DAL.Interfaces;
using Restaurant.DAL.Models;
using Shared.Persistence.Repositories;

namespace Restaurant.DAL.Implements;

public sealed class TableSessionRepository(RestaurantDbContext db)
    : GenericRepository<TableSession, RestaurantDbContext>(db), ITableSessionRepository
{
    public Task<List<TableSession>> GetBillingDisplaysAsync(Guid restaurantId, IReadOnlyList<Guid> ids, CancellationToken ct)
    {
        return _context.TableSessions.AsNoTracking()
            .Where(session => ids.Contains(session.TableSessionId)
                && session.SessionTables.Any(link => link.Table.Area.RestaurantId == restaurantId)
                && !session.SessionTables.Any(link => link.Table.Area.RestaurantId != restaurantId))
            .Include(session => session.SessionTables).ThenInclude(link => link.Table).ThenInclude(table => table.Area)
            .AsSplitQuery().ToListAsync(ct);
    }

    public Task<TableSession?> GetForBillingAsync(Guid id, CancellationToken ct)
    {
        return _context.TableSessions.AsNoTracking()
            .Include(session => session.SessionTables)
                .ThenInclude(link => link.Table).ThenInclude(table => table.Area)
            .SingleOrDefaultAsync(session => session.TableSessionId == id, ct);
    }

    public Task<TableSession?> GetForOrderingAsync(Guid id, CancellationToken ct)
    {
        return WithActiveTables(_context.TableSessions.AsNoTracking())
            .SingleOrDefaultAsync(session => session.TableSessionId == id, ct);
    }

    public async Task<TableSession?> LockForTransferAsync(Guid id, CancellationToken ct)
    {
        var session = await LockAsync(id, ct);
        if (session is not null)
        {
            // Refresh the instance that may have been read before waiting on the SQL lock.
            await _context.Entry(session).ReloadAsync(ct);
        }
        return session;
    }

    public Task<TableSession?> LockAsync(Guid id, CancellationToken ct)
    {
        RequireTransaction();
        if (_context.Database.IsSqlServer())
        {
            return LockedSession(id).SingleOrDefaultAsync(ct);
        }

        return GetByIdAsync(id, ct);
    }

    public Task<TableSession?> LockWithActiveTablesAsync(Guid id, CancellationToken ct)
    {
        RequireTransaction();
        var query = _context.Database.IsSqlServer()
            ? LockedSession(id) : _context.TableSessions.Where(session => session.TableSessionId == id);
        return WithActiveTables(query.AsNoTracking()).SingleOrDefaultAsync(ct);
    }

    public Task<List<SessionTable>> GetUnreleasedLinksAsync(Guid id, CancellationToken ct)
    {
        return _context.SessionTables.Where(link => link.TableSessionId == id && link.ReleasedAt == null).ToListAsync(ct);
    }

    private IQueryable<TableSession> LockedSession(Guid id)
    {
        return _context.TableSessions.FromSqlInterpolated(
            $"SELECT * FROM [TableSession] WITH (UPDLOCK, HOLDLOCK) WHERE [TableSessionId] = {id}");
    }

    private static IQueryable<TableSession> WithActiveTables(IQueryable<TableSession> query)
    {
        return query.Include(session => session.SessionTables.Where(link => link.ReleasedAt == null))
            .ThenInclude(link => link.Table).ThenInclude(table => table.Area);
    }

    private void RequireTransaction()
    {
        if (_context.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("Cần có giao dịch để khóa phiên bàn.");
        }
    }

    public Task<List<TableSession>> GetOpenForTableAsync(Guid tableId, CancellationToken ct)
    {
        return _context.TableSessions.Where(x => x.ClosedAt == null
            && x.SessionTables.Any(link => link.TableId == tableId && link.ReleasedAt == null))
            .Take(2).ToListAsync(ct);
    }
}
