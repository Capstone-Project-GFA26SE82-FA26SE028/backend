using Microsoft.EntityFrameworkCore;
using Restaurant.DAL.Interfaces;
using Restaurant.DAL.Models;
using Shared.Persistence.Repositories;

namespace Restaurant.DAL.Implements;

public sealed class RestaurantStaffRepository(RestaurantDbContext db)
    : GenericRepository<RestaurantStaff, RestaurantDbContext>(db), IRestaurantStaffRepository
{
    public Task<RestaurantStaff?> LockAsync(Guid restaurantId, Guid userId, CancellationToken ct)
    {
        if (_context.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("An active transaction is required to lock an assignment.");
        }

        var source = _context.Database.IsSqlServer()
            ? _context.RestaurantStaffs.FromSqlInterpolated($"SELECT * FROM dbo.RestaurantStaff WITH (UPDLOCK, HOLDLOCK) WHERE RestaurantId = {restaurantId} AND UserIdRef = {userId}")
            : _context.RestaurantStaffs.Where(value => value.RestaurantId == restaurantId && value.UserIdRef == userId);
        return source.SingleOrDefaultAsync(ct);
    }

    public Task<bool> IsActiveAsync(Guid restaurantId, Guid userId, CancellationToken ct)
    {
        return _context.RestaurantStaffs.AsNoTracking().AnyAsync(value => value.RestaurantId == restaurantId
            && value.UserIdRef == userId && value.IsActive, ct);
    }

    public async Task<IReadOnlyList<Guid>> GetRestaurantIdsAsync(Guid userId, CancellationToken ct)
    {
        return await _context.RestaurantStaffs.AsNoTracking().Where(value => value.UserIdRef == userId && value.IsActive)
            .Select(value => value.RestaurantId).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<RestaurantStaff>> GetAllAsync(Guid restaurantId, CancellationToken ct)
    {
        return await _context.RestaurantStaffs.AsNoTracking().Where(value => value.RestaurantId == restaurantId)
            .OrderBy(value => value.UserIdRef).ToListAsync(ct);
    }
}
