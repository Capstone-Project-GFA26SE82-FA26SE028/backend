using Identity.DAL.Interfaces;
using Identity.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace Identity.DAL.Implements;

public sealed class AnnouncementRepository(IdentityDbContext db) : IAnnouncementRepository
{
    public async Task<(List<Announcement> Items, int Total)> GetAllAsync(Guid restaurantId, int page, int pageSize, CancellationToken ct)
    {
        var query = db.Announcements.Where(value => value.RestaurantId == restaurantId);
        return (await query.AsNoTracking().OrderByDescending(value => value.CreatedAt).ThenBy(value => value.AnnouncementId)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct), await query.CountAsync(ct));
    }

    public Task<Announcement?> GetByIdAsync(Guid restaurantId, Guid id, CancellationToken ct)
    {
        return db.Announcements.SingleOrDefaultAsync(value => value.RestaurantId == restaurantId && value.AnnouncementId == id, ct);
    }

    public void Create(Announcement value)
    {
        db.Announcements.Add(value);
    }

    public void Delete(Announcement value)
    {
        db.Announcements.Remove(value);
    }
}
