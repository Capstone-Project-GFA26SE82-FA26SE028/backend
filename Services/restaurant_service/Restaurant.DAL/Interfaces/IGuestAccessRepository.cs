using Restaurant.DAL.Models;
using Shared.Persistence.Interfaces;

namespace Restaurant.DAL.Interfaces;

public interface IGuestAccessRepository : IGenericRepository<GuestAccess>
{
    Task<GuestAccess?> FindByTokenHashAsync(string hash, CancellationToken ct);
    Task<List<GuestAccess>> GetUnrevokedBySessionAsync(Guid sessionId, CancellationToken ct);
}

