using Identity.BLL.Interfaces;
using Identity.DAL.Interfaces;
using Identity.DAL.UnitOfWorks;
using Identity.Domain.Models;
using Shared.Common.Notifications;
using Shared.Common.Pagination;

namespace Identity.BLL.Implements;

public sealed class NotificationService(INotificationRepository repository, INotificationAccess access,
    IUnitOfWork work, INotificationAudience audience) : INotificationService
{
    public static void ValidateQuery(NotificationQuery query)
    {
        if (query.Page < 1 || query.Page > 1000000 || query.PageSize is < 1 or > 100
            || (!string.IsNullOrWhiteSpace(query.Type) && !NotificationCatalog.Types.ContainsKey(query.Type))
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

    public async Task Publish(NotificationEvent request, CancellationToken ct)
    {
        if (!NotificationCatalog.Types.TryGetValue(request.Type, out var definition) || request.Type == "Announcement"
            || request.RestaurantId == Guid.Empty || request.EntityId == Guid.Empty
            || string.IsNullOrWhiteSpace(request.EventKey) || request.EventKey.Length > 200
            || request.OccurredAt == default || (definition.Customer && (!request.CustomerId.HasValue || request.CustomerId == Guid.Empty))
            || (request.Type == "KitchenOrderAccepted" && (!request.KitchenUserId.HasValue || request.KitchenUserId == Guid.Empty)))
        {
            throw new NotificationException(400, "Sự kiện thông báo không hợp lệ.");
        }

        await work.ExecuteInTransactionAsync(async () =>
        {
            if (await repository.ExistsAsync(request.EventKey, ct))
            {
                return true;
            }

            var recipients = await repository.GetRecipientsAsync(request.RestaurantId, definition.Roles,
                definition.Customer ? request.CustomerId : null, request.KitchenUserId, ct);
            if (definition.Roles.Length > 0)
            {
                var activeStaff = await audience.GetStaffAsync(request.RestaurantId, ct);
                recipients = recipients.Where(user => user.RoleId == "customer" || activeStaff.Contains(user.UserId)).ToList();
            }

            if (request.Type == "KitchenOrderAccepted" && recipients.Count == 0)
            {
                throw new NotificationException(503, "Nhân viên phụ trách bếp chưa hoạt động hoặc chưa được phân công; hệ thống sẽ thử lại.");
            }

            repository.Create(new Notification
            {
                NotificationId = Guid.NewGuid(),
                RestaurantId = request.RestaurantId,
                EventKey = request.EventKey,
                Type = request.Type,
                Title = definition.Title,
                Message = definition.Message,
                EntityType = definition.EntityType,
                EntityId = request.EntityId,
                CreatedAt = request.OccurredAt,
                Recipients = recipients.Select(user => new NotificationRecipient
                {
                    UserId = user.UserId,
                    IsCustomer = user.RoleId == "customer"
                }).ToList()
            });
            return true;
        }, ct);
    }

    private static NotificationItem Map(Notification value, Guid? userId)
    {
        return new NotificationItem(value.NotificationId, value.RestaurantId, value.Type, value.Title, value.Message,
            value.EntityType, value.EntityId, DateTime.SpecifyKind(value.CreatedAt, DateTimeKind.Utc),
            value.Recipients.FirstOrDefault(recipient => recipient.UserId == userId)?.ReadAt is { } read
                ? DateTime.SpecifyKind(read, DateTimeKind.Utc) : null);
    }
}
