using Identity.BLL.Interfaces;
using Identity.DAL.Interfaces;
using Identity.DAL.UnitOfWorks;
using Identity.Domain.Models;
using Shared.Common.Notifications;
using Shared.Common.Pagination;

namespace Identity.BLL.Implements;

public sealed class AnnouncementService(IAnnouncementRepository repository, INotificationRepository notifications,
    INotificationSettingRepository settings, INotificationAccess access, IUnitOfWork work, INotificationAudience audience) : IAnnouncementService
{
    public async Task<AnnouncementItem> Create(Guid userId, Guid restaurantId, AnnouncementWrite request, CancellationToken ct)
    {
        await access.RequireAdmin(userId, restaurantId, ct);
        Validate(request);
        return await work.ExecuteInTransactionAsync(() =>
        {
            var value = new Announcement
            {
                AnnouncementId = Guid.NewGuid(),
                RestaurantId = restaurantId,
                CreatedBy = userId,
                CreatedAt = DateTime.UtcNow
            };
            Apply(value, request);
            repository.Create(value);
            work.RecordAudit(userId, "CREATE_ANNOUNCEMENT", "Announcement", value.AnnouncementId.ToString());
            return Task.FromResult(Map(value));
        }, ct);
    }

    public async Task<PagedResult<AnnouncementItem>> GetAll(Guid userId, Guid restaurantId, int page, int pageSize, CancellationToken ct)
    {
        await access.RequireAdmin(userId, restaurantId, ct);
        NotificationService.ValidateQuery(new NotificationQuery
        {
            Page = page,
            PageSize = pageSize
        });
        var (items, total) = await repository.GetAllAsync(restaurantId, page, pageSize, ct);
        return new PagedResult<AnnouncementItem>(items.Select(Map).ToList(), total, page, pageSize);
    }

    public async Task<AnnouncementItem> GetById(Guid userId, Guid restaurantId, Guid id, CancellationToken ct)
    {
        await access.RequireAdmin(userId, restaurantId, ct);
        return Map(await Find(restaurantId, id, ct));
    }

    public async Task<AnnouncementItem> Update(Guid userId, Guid restaurantId, Guid id, AnnouncementWrite request, CancellationToken ct)
    {
        await access.RequireAdmin(userId, restaurantId, ct);
        Validate(request);
        return await work.ExecuteInTransactionAsync(async () =>
        {
            var value = await Find(restaurantId, id, ct);
            RequireDraft(value, request.Version ?? Guid.Empty);
            Apply(value, request);
            work.RecordAudit(userId, "UPDATE_ANNOUNCEMENT", "Announcement", id.ToString());
            return Map(value);
        }, ct);
    }

    public async Task Delete(Guid userId, Guid restaurantId, Guid id, Guid version, CancellationToken ct)
    {
        await access.RequireAdmin(userId, restaurantId, ct);
        await work.ExecuteInTransactionAsync(async () =>
        {
            var value = await Find(restaurantId, id, ct);
            RequireDraft(value, version);
            repository.Delete(value);
            work.RecordAudit(userId, "DELETE_ANNOUNCEMENT", "Announcement", id.ToString());
            return true;
        }, ct);
    }

    public async Task<AnnouncementItem> Send(Guid userId, Guid restaurantId, Guid id, Guid version, CancellationToken ct)
    {
        await access.RequireAdmin(userId, restaurantId, ct);
        return await work.ExecuteInTransactionAsync(async () =>
        {
            var value = await Find(restaurantId, id, ct);
            if (value.SentAt.HasValue)
            {
                return Map(value);
            }

            RequireDraft(value, version);
            if ((await settings.GetByTypeAsync(restaurantId, "Announcement", ct))?.Enabled == false)
            {
                throw new NotificationException(409, "Nhà hàng đang tắt thông báo nội bộ. Vui lòng bật trước khi gửi.");
            }

            var roles = value.AllStaff ? NotificationCatalog.StaffRoles : value.RecipientRoles.Split(',', StringSplitOptions.RemoveEmptyEntries);
            var recipients = await notifications.GetRecipientsAsync(restaurantId, roles, null, null, ct);
            var activeStaff = await audience.GetStaffAsync(restaurantId, ct);
            recipients = recipients.Where(user => activeStaff.Contains(user.UserId)).ToList();

            if (recipients.Count == 0)
            {
                throw new NotificationException(409, "Không có nhân viên đang hoạt động phù hợp để nhận thông báo.");
            }

            var notification = new Notification
            {
                NotificationId = Guid.NewGuid(),
                RestaurantId = restaurantId,
                EventKey = $"Announcement:{id:N}",
                Type = "Announcement",
                Title = value.Title,
                Message = value.Message,
                EntityType = "Announcement",
                EntityId = id,
                CreatedAt = DateTime.UtcNow,
                Recipients = recipients.Select(user => new NotificationRecipient
                {
                    UserId = user.UserId
                }).ToList()
            };
            notifications.Create(notification);
            value.NotificationId = notification.NotificationId;
            value.SentAt = notification.CreatedAt;
            value.Version = Guid.NewGuid();
            work.RecordAudit(userId, "SEND_ANNOUNCEMENT", "Announcement", id.ToString());
            return Map(value);
        }, ct);
    }

    private async Task<Announcement> Find(Guid restaurantId, Guid id, CancellationToken ct)
    {
        return await repository.GetByIdAsync(restaurantId, id, ct)
            ?? throw new NotificationException(404, "Không tìm thấy thông báo nội bộ.");
    }

    private static void Validate(AnnouncementWrite request)
    {
        if (string.IsNullOrWhiteSpace(request.Title) || request.Title.Length > 200 || request.Title.Any(char.IsControl)
            || string.IsNullOrWhiteSpace(request.Message) || request.Message.Length > 2000
            || request.RecipientRoleIds is null || request.RecipientRoleIds.Length > NotificationCatalog.StaffRoles.Length
            || (request.AllStaff ? request.RecipientRoleIds.Length != 0 : request.RecipientRoleIds.Length == 0)
            || request.RecipientRoleIds.Any(role => !NotificationCatalog.StaffRoles.Contains(role)))
        {
            throw new NotificationException(400, "Nhập tiêu đề tối đa 200 ký tự, nội dung tối đa 2000 ký tự và chọn toàn bộ nhân viên hoặc các vai trò hợp lệ.");
        }
    }

    private static void RequireDraft(Announcement value, Guid version)
    {
        if (value.SentAt.HasValue || version == Guid.Empty || value.Version != version)
        {
            throw new NotificationException(409, "Thông báo đã gửi hoặc phiên bản đã thay đổi. Vui lòng tải lại.");
        }
    }

    private static void Apply(Announcement value, AnnouncementWrite request)
    {
        value.Title = request.Title.Trim();
        value.Message = request.Message.Trim();
        value.AllStaff = request.AllStaff;
        value.RecipientRoles = string.Join(',', request.RecipientRoleIds.Distinct().Order());
        value.Version = Guid.NewGuid();
    }

    private static AnnouncementItem Map(Announcement value)
    {
        return new AnnouncementItem(value.AnnouncementId, value.RestaurantId, value.Title, value.Message, value.AllStaff,
            value.RecipientRoles.Split(',', StringSplitOptions.RemoveEmptyEntries), value.Version,
            value.SentAt.HasValue ? "Sent" : "Draft", DateTime.SpecifyKind(value.CreatedAt, DateTimeKind.Utc),
            value.SentAt is { } sent ? DateTime.SpecifyKind(sent, DateTimeKind.Utc) : null, value.NotificationId);
    }
}
