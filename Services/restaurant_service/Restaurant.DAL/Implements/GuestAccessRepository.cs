using Microsoft.EntityFrameworkCore;
using Restaurant.DAL.Interfaces;
using Restaurant.DAL.Models;
using Shared.Persistence.Repositories;

namespace Restaurant.DAL.Implements;

public sealed class GuestAccessRepository(RestaurantDbContext db)
    : GenericRepository<GuestAccess, RestaurantDbContext>(db), IGuestAccessRepository
{
    public async Task<IReadOnlyList<GuestAccess>> GetUnrevokedForSessionAsync(Guid sessionId, CancellationToken ct)
    {
        return await _context.GuestAccesses.Where(value => value.TableSessionId == sessionId && value.RevokedAt == null).ToListAsync(ct);
    }

    public Task<GuestAccess?> FindByTokenHashAsync(string hash, CancellationToken ct)
    {
        return _context.GuestAccesses.Include(x => x.TableSession)
            .SingleOrDefaultAsync(x => x.TokenHash == hash, ct);
    }
}
