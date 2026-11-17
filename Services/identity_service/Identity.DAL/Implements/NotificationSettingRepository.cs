using Identity.DAL.Interfaces;
using Identity.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace Identity.DAL.Implements;

public sealed class NotificationSettingRepository(IdentityDbContext db) : INotificationSettingRepository
{
    public Task<List<NotificationSetting>> GetAllAsync(Guid restaurantId, CancellationToken ct)
    {
        return db.NotificationSettings.Where(value => value.RestaurantId == restaurantId).AsNoTracking().ToListAsync(ct);
    }

    public Task<NotificationSetting?> GetByTypeAsync(Guid restaurantId, string type, CancellationToken ct)
    {
        return db.NotificationSettings.SingleOrDefaultAsync(value => value.RestaurantId == restaurantId && value.Type == type, ct);
    }

    public void Create(NotificationSetting value)
    {
        db.NotificationSettings.Add(value);
    }
}
