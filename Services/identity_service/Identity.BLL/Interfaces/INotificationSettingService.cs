using Shared.Common.Notifications;

namespace Identity.BLL.Interfaces;

public interface INotificationSettingService
{
    Task<IReadOnlyList<NotificationSettingItem>> GetAll(Guid userId, Guid restaurantId, CancellationToken ct);
    Task<NotificationSettingItem> Update(Guid userId, Guid restaurantId, string type, bool enabled, CancellationToken ct);
}
