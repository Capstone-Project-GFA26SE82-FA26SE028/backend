using Microsoft.EntityFrameworkCore;
using Restaurant.DAL.Interfaces;
using Restaurant.DAL.Models;
using Shared.Persistence.Repositories;

namespace Restaurant.DAL.Implements;

public sealed class TableSessionRepository(RestaurantDbContext db)
    : GenericRepository<TableSession, RestaurantDbContext>(db), ITableSessionRepository
{
    public async Task<(IReadOnlyList<Guid> Ids, int Total)> GetSettlementCandidatesAsync(int skip, int take, CancellationToken ct)
    {
        var query = _context.TableSessions.AsNoTracking().Where(session => session.Status == "Open" && session.ClosedAt == null
            && session.ServiceRequests.Any(request => request.Type == "Payment" && request.Status == "Reviewed"));
        var total = await query.CountAsync(ct);
        var ids = await query.OrderBy(session => session.TableSessionId).Skip(skip).Take(take)
            .Select(session => session.TableSessionId).ToListAsync(ct);
        return (ids, total);
    }

    public Task<TableSession?> GetForDepartureAsync(Guid id, CancellationToken ct)
    {
        return _context.TableSessions.Include(session => session.SessionTables)
            .ThenInclude(link => link.Table).ThenInclude(table => table.Area)
            .SingleOrDefaultAsync(session => session.TableSessionId == id, ct);
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
        return _context.TableSessions.AsNoTracking()
            .Include(session => session.SessionTables.Where(link => link.ReleasedAt == null))
                .ThenInclude(link => link.Table).ThenInclude(table => table.Area)
            .SingleOrDefaultAsync(session => session.TableSessionId == id, ct);
    }

    public Task<TableSession?> LockAsync(Guid id, CancellationToken ct)
    {
        if (_context.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("Cần có giao dịch để khóa phiên bàn.");
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
