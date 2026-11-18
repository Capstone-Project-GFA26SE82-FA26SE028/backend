using Microsoft.EntityFrameworkCore;
using Restaurant.DAL.Interfaces;
using Restaurant.DAL.Models;
using Shared.Persistence.Repositories;

namespace Restaurant.DAL.Implements;

public sealed class GuestAccessRepository(RestaurantDbContext db)
    : GenericRepository<GuestAccess, RestaurantDbContext>(db), IGuestAccessRepository
{
    public Task<GuestAccess?> FindByTokenHashAsync(string hash, CancellationToken ct)
    {
        return _context.GuestAccesses.Include(x => x.TableSession)
            .SingleOrDefaultAsync(x => x.TokenHash == hash, ct);
    }

    public Task<List<GuestAccess>> GetUnrevokedBySessionAsync(Guid sessionId, CancellationToken ct)
    {
        return _context.GuestAccesses.Where(value => value.TableSessionId == sessionId && value.RevokedAt == null).ToListAsync(ct);
    }
}
