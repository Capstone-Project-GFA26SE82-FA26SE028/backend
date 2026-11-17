using Identity.BLL.Interfaces;
using Identity.DAL.Interfaces;
using Identity.DAL.UnitOfWorks;
using Identity.Domain.Models;
using Shared.Common.Notifications;

namespace Identity.BLL.Implements;

public sealed class NotificationSettingService(INotificationSettingRepository repository, INotificationAccess access,
    IUnitOfWork work) : INotificationSettingService
{
    public async Task<IReadOnlyList<NotificationSettingItem>> GetAll(Guid userId, Guid restaurantId, CancellationToken ct)
    {
        await access.RequireAdmin(userId, restaurantId, ct);
        var settings = await repository.GetAllAsync(restaurantId, ct);
        return NotificationCatalog.Types.Select(pair => new NotificationSettingItem(pair.Key, pair.Value.Title,
            pair.Value.Required, pair.Value.Required || settings.FirstOrDefault(value => value.Type == pair.Key)?.Enabled != false,
            pair.Value.Roles, pair.Value.Customer)).ToArray();
    }

    public async Task<NotificationSettingItem> Update(Guid userId, Guid restaurantId, string type, bool enabled, CancellationToken ct)
    {
        await access.RequireAdmin(userId, restaurantId, ct);
        if (!NotificationCatalog.Types.TryGetValue(type, out var definition))
        {
            throw new NotificationException(400, "Loại thông báo không hợp lệ.");
        }
        if (definition.Required && !enabled)
        {
            throw new NotificationException(409, "Không được tắt thông báo nghiệp vụ bắt buộc.");
        }

        return await work.ExecuteInTransactionAsync(async () =>
        {
            var value = await repository.GetByTypeAsync(restaurantId, type, ct);
            if (value is null)
            {
                value = new NotificationSetting
                {
                    RestaurantId = restaurantId,
                    Type = type
                };
                repository.Create(value);
            }

            value.Enabled = enabled;
            work.RecordAudit(userId, "UPDATE_NOTIFICATION_SETTING", "NotificationSetting", $"{restaurantId}:{type}", null, new { Enabled = enabled });
            return new NotificationSettingItem(type, definition.Title, definition.Required, enabled, definition.Roles, definition.Customer);
        }, ct);
    }
}
