using Shared.Common.Notifications;
using Shared.Common.Pagination;

namespace Identity.BLL.Interfaces;

public interface INotificationService
{
    Task<PagedResult<NotificationItem>> GetAll(Guid userId, Guid? restaurantId, NotificationQuery query, CancellationToken ct);
    Task<NotificationDetails> GetById(Guid userId, Guid? restaurantId, Guid id, CancellationToken ct);
    Task<int> UnreadCount(Guid userId, CancellationToken ct);
    Task MarkRead(Guid userId, Guid? id, CancellationToken ct);
}
