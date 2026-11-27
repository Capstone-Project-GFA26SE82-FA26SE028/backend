using Microsoft.EntityFrameworkCore;
using Restaurant.DAL.Interfaces;
using Restaurant.DAL.Models;

namespace Restaurant.DAL.Implements;

public sealed class RestaurantRegistrationRepository(RestaurantDbContext db) : IRestaurantRegistrationRepository
{
    public Task<RestaurantRegistration?> GetByIdAsync(Guid id, CancellationToken ct)
    {
        return db.RestaurantRegistrations.SingleOrDefaultAsync(item => item.RegistrationId == id, ct);
    }

    public Task<RestaurantRegistration?> GetForApplicantAsync(Guid userId, CancellationToken ct)
    {
        return db.RestaurantRegistrations.AsNoTracking().SingleOrDefaultAsync(item => item.ApplicantUserRef == userId, ct);
    }

    public async Task<(List<RestaurantRegistration> Items, int TotalCount)> GetAllAsync(string? status,
        int page, int pageSize, CancellationToken ct)
    {
        var query = db.RestaurantRegistrations.AsNoTracking();
        if (status is not null)
        {
            query = query.Where(item => item.Status == status);
        }

        var count = await query.CountAsync(ct);
        var items = await query.OrderByDescending(item => item.SubmittedAt).ThenBy(item => item.RegistrationId)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return (items, count);
    }

    public void Create(RestaurantRegistration registration)
    {
        db.RestaurantRegistrations.Add(registration);
    }
}
