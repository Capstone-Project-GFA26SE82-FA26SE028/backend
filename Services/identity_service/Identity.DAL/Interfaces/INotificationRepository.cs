using Identity.Domain.Models;
using Shared.Common.Notifications;

namespace Identity.DAL.Interfaces;

public interface INotificationRepository
{
    Task<(List<Notification> Items, int Total)> GetAllAsync(Guid? restaurantId, Guid? userId, Guid? staffRestaurantId, bool customer, NotificationQuery query, CancellationToken ct);
    Task<Notification?> GetByIdAsync(Guid id, Guid? restaurantId, Guid? userId, Guid? staffRestaurantId, bool customer, CancellationToken ct);
    Task<int> CountUnreadAsync(Guid userId, Guid? staffRestaurantId, bool customer, CancellationToken ct);
    Task<int> MarkReadAsync(Guid userId, Guid? staffRestaurantId, bool customer, Guid? id, CancellationToken ct);
    Task<bool> ExistsAsync(string eventKey, CancellationToken ct);
    Task<List<User>> GetRecipientsAsync(Guid restaurantId, string[] roles, Guid? customerId, Guid? kitchenUserId, CancellationToken ct);
    Task<List<NotificationReader>> GetReadersAsync(Guid id, CancellationToken ct);
    void Create(Notification value);
}
