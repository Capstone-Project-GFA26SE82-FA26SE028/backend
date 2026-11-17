using Identity.BLL.Interfaces;
using Identity.DAL.Interfaces;
using Identity.DAL.UnitOfWorks;
using Identity.Domain.Models;
using Shared.Common.Notifications;
using Shared.Common.Pagination;

namespace Identity.BLL.Implements;

public sealed class NotificationService(INotificationRepository repository, INotificationAccess access,
    IUnitOfWork work) : INotificationService
{
    public static void ValidateQuery(NotificationQuery query)
    {
        if (query.Page < 1 || query.Page > 1000000 || query.PageSize is < 1 or > 100
            || (query.Type is not null && !NotificationCatalog.Types.ContainsKey(query.Type))
            || query.From.HasValue && query.To.HasValue && query.From >= query.To)
        {
            throw new NotificationException(400, "Bộ lọc, thời gian hoặc phân trang không hợp lệ.");
        }
    }

    public async Task<PagedResult<NotificationItem>> GetAll(Guid userId, Guid? restaurantId, NotificationQuery query, CancellationToken ct)
    {
        ValidateQuery(query);
        var actor = await access.GetActor(userId, ct);
        if (restaurantId.HasValue)
        {
            await access.RequireAdmin(userId, restaurantId.Value, ct);
            if (query.IsRead.HasValue)
            {
                throw new NotificationException(400, "Bộ lọc đã đọc chỉ áp dụng cho thông báo cá nhân.");
            }
        }

        var (items, total) = await repository.GetAllAsync(restaurantId, restaurantId.HasValue ? null : userId,
            actor.RestaurantId, actor.Customer, query, ct);
        return new PagedResult<NotificationItem>(items.Select(value => Map(value, restaurantId.HasValue ? null : userId)).ToList(),
            total, query.Page, query.PageSize);
    }

    public async Task<NotificationDetails> GetById(Guid userId, Guid? restaurantId, Guid id, CancellationToken ct)
    {
        var actor = await access.GetActor(userId, ct);
        if (restaurantId.HasValue)
        {
            await access.RequireAdmin(userId, restaurantId.Value, ct);
        }

        var value = await repository.GetByIdAsync(id, restaurantId, restaurantId.HasValue ? null : userId,
            actor.RestaurantId, actor.Customer, ct) ?? throw new NotificationException(404, "Không tìm thấy thông báo.");
        var readers = restaurantId.HasValue ? await repository.GetReadersAsync(id, ct) : [];
        return new NotificationDetails(Map(value, restaurantId.HasValue ? null : userId), readers);
    }

    public async Task<int> UnreadCount(Guid userId, CancellationToken ct)
    {
        var actor = await access.GetActor(userId, ct);
        return await repository.CountUnreadAsync(userId, actor.RestaurantId, actor.Customer, ct);
    }

    public async Task MarkRead(Guid userId, Guid? id, CancellationToken ct)
    {
        var actor = await access.GetActor(userId, ct);
        if (id.HasValue)
        {
            await GetById(userId, null, id.Value, ct);
        }

        await repository.MarkReadAsync(userId, actor.RestaurantId, actor.Customer, id, ct);
    }


    private static NotificationItem Map(Notification value, Guid? userId)
    {
        return new NotificationItem(value.NotificationId, value.RestaurantId, value.Type, value.Title, value.Message,
            value.EntityType, value.EntityId, DateTime.SpecifyKind(value.CreatedAt, DateTimeKind.Utc),
            value.Recipients.FirstOrDefault(recipient => recipient.UserId == userId)?.ReadAt is { } read
                ? DateTime.SpecifyKind(read, DateTimeKind.Utc) : null);
    }
}
