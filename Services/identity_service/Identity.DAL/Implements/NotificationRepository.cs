using Identity.DAL.Interfaces;
using Identity.Domain.Models;
using Microsoft.EntityFrameworkCore;
using Shared.Common.Notifications;

namespace Identity.DAL.Implements;

public sealed class NotificationRepository(IdentityDbContext db) : INotificationRepository
{
    private IQueryable<NotificationRecipient> Visible(Guid userId, Guid? staffRestaurantId, bool customer)
    {
        return db.NotificationRecipients.Where(value => value.UserId == userId
            && (customer ? value.IsCustomer : !value.IsCustomer && value.Notification.RestaurantId == staffRestaurantId));
    }

    private IQueryable<Notification> Query(Guid? restaurantId, Guid? userId, Guid? staffRestaurantId, bool customer)
    {
        return userId.HasValue
            ? db.Notifications.Where(value => Visible(userId.Value, staffRestaurantId, customer).Select(recipient => recipient.NotificationId).Contains(value.NotificationId))
            : db.Notifications.Where(value => value.RestaurantId == restaurantId && !value.Suppressed);
    }

    public async Task<(List<Notification> Items, int Total)> GetAllAsync(Guid? restaurantId, Guid? userId, Guid? staffRestaurantId, bool customer, NotificationQuery filter, CancellationToken ct)
    {
        var query = Query(restaurantId, userId, staffRestaurantId, customer);
        if (!string.IsNullOrWhiteSpace(filter.Type))
        {
            query = query.Where(value => value.Type == filter.Type);
        }
        if (filter.EntityId.HasValue)
        {
            query = query.Where(value => value.EntityId == filter.EntityId);
        }
        if (filter.From.HasValue)
        {
            var from = filter.From.Value.UtcDateTime;
            query = query.Where(value => value.CreatedAt >= from);
        }
        if (filter.To.HasValue)
        {
            var to = filter.To.Value.UtcDateTime;
            query = query.Where(value => value.CreatedAt < to);
        }
        if (filter.IsRead.HasValue && userId.HasValue)
        {
            var read = filter.IsRead.Value;
            query = query.Where(value => value.Recipients.Any(recipient => recipient.UserId == userId && recipient.ReadAt.HasValue == read));
        }
        var total = await query.CountAsync(ct);
        var items = await query.AsNoTracking().Include(value => value.Recipients)
            .OrderByDescending(value => value.CreatedAt).ThenByDescending(value => value.NotificationId)
            .Skip((filter.Page - 1) * filter.PageSize).Take(filter.PageSize).ToListAsync(ct);
        return (items, total);
    }

    public Task<Notification?> GetByIdAsync(Guid id, Guid? restaurantId, Guid? userId, Guid? staffRestaurantId, bool customer, CancellationToken ct)
    {
        return Query(restaurantId, userId, staffRestaurantId, customer).AsNoTracking()
            .Include(value => value.Recipients).SingleOrDefaultAsync(value => value.NotificationId == id, ct);
    }

    public Task<int> CountUnreadAsync(Guid userId, Guid? staffRestaurantId, bool customer, CancellationToken ct)
    {
        return Visible(userId, staffRestaurantId, customer).CountAsync(value => value.ReadAt == null, ct);
    }

    public Task<int> MarkReadAsync(Guid userId, Guid? staffRestaurantId, bool customer, Guid? id, CancellationToken ct)
    {
        var query = Visible(userId, staffRestaurantId, customer).Where(value => value.ReadAt == null);
        if (id.HasValue)
        {
            query = query.Where(value => value.NotificationId == id);
        }
        return query.ExecuteUpdateAsync(set => set.SetProperty(value => value.ReadAt, (DateTime?)DateTime.UtcNow), ct);
    }

    public Task<bool> ExistsAsync(string eventKey, CancellationToken ct)
    {
        return db.Notifications.AnyAsync(value => value.EventKey == eventKey, ct);
    }

    public Task<List<User>> GetRecipientsAsync(Guid restaurantId, string[] roles, Guid? customerId, Guid? kitchenUserId, CancellationToken ct)
    {
        return db.Users.AsNoTracking().Where(user => user.IsActive && !user.ProvisioningPending
            && ((user.UserId == customerId && user.RoleId == "customer")
                || (user.RestaurantId == restaurantId && roles.Contains(user.RoleId)
                    && (user.RoleId != "kitchen" || !kitchenUserId.HasValue || user.UserId == kitchenUserId))))
            .ToListAsync(ct);
    }

    public async Task<List<NotificationReader>> GetReadersAsync(Guid id, CancellationToken ct)
    {
        var readers = await (from recipient in db.NotificationRecipients
                join user in db.Users on recipient.UserId equals user.UserId
                where recipient.NotificationId == id
                orderby user.FullName, user.UserId
                select new NotificationReader(user.UserId, user.FullName, recipient.ReadAt)).ToListAsync(ct);
        return readers.Select(value => value with
        {
            ReadAt = value.ReadAt is { } read ? DateTime.SpecifyKind(read, DateTimeKind.Utc) : null
        }).ToList();
    }

    public void Create(Notification value)
    {
        db.Notifications.Add(value);
    }
}
