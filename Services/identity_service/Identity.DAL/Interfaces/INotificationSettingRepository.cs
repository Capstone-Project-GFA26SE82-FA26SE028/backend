using Identity.Domain.Models;

namespace Identity.DAL.Interfaces;

public interface INotificationSettingRepository
{
    Task<List<NotificationSetting>> GetAllAsync(Guid restaurantId, CancellationToken ct);
    Task<NotificationSetting?> GetByTypeAsync(Guid restaurantId, string type, CancellationToken ct);
    void Create(NotificationSetting value);
}
