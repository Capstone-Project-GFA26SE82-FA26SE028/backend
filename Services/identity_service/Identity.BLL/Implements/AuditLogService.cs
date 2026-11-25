using Shared.Common.Pagination;
using Shared.Common.DTOs;
using Identity.BLL.Interfaces;
using Identity.Common.DTOs;
using Identity.Common.Settings;
using Identity.DAL.Interfaces;
using Microsoft.Extensions.Options;

namespace Identity.BLL.Implements;

public sealed class AuditLogService(
    IAuditLogRepository repository,
    IUserRepository users,
    IOptions<UserManagementSettings> settings) : IAuditLogService
{
    public async Task<ResponseDTO> GetAll(Guid actorId, int page, int pageSize, Guid? userId, CancellationToken ct, Identity.Common.DTOs.AuditLogFilter? filter = null)
    {
        var actor = await users.FindUserAsync(actorId, ct);
        if (actor is not { IsActive: true })
        {
            return new ResponseDTO("Truy cập bị từ chối.", 403);
        }

        var isAdmin = settings.Value.AdminRoleIds.Contains(actor.RoleId, StringComparer.Ordinal);
        var isManager = settings.Value.ManagerRoleIds.Contains(actor.RoleId, StringComparer.Ordinal);

        if (!isAdmin && !isManager)
        {
            return new ResponseDTO("Truy cập bị từ chối.", 403);
        }

        if (isManager && !isAdmin)
        {
            if (!actor.RestaurantId.HasValue)
            {
                return new ResponseDTO("Tài khoản quản lý chưa được gán nhà hàng.", 403);
            }

            filter ??= new Identity.Common.DTOs.AuditLogFilter();
            filter.RestaurantId = actor.RestaurantId.Value;
        }

        var pagination = new PaginationRequest
        {
            Page = page,
            PageSize = pageSize
        };

        if (!pagination.IsValid() || userId == Guid.Empty || filter?.Search?.Length > 255
            || filter?.Action?.Length > 100 || (filter?.FromUtc.HasValue == true
                && filter.ToUtc.HasValue && filter.FromUtc > filter.ToUtc))
        {
            return new ResponseDTO("Phân trang hoặc mã người dùng không hợp lệ. Số lượng mỗi trang phải từ 1 đến 100.", 400);
        }

        var (items, total) = await repository.GetAllAsync(page, pageSize, userId, ct, filter);
        var result = new PagedResult<AuditLogDTO>
        {
            Items = items.Select(item => new AuditLogDTO
            {
                AuditLogId = item.AuditLogId,
                UserId = item.UserId,
                FullName = item.User?.FullName,
                UserName = item.User?.UserName,
                Email = item.User?.Email,
                AvatarUrl = item.User?.AvatarUrl,
                Action = item.Action,
                EntityName = item.EntityName,
                EntityId = item.EntityId,
                OldValue = item.OldValue,
                NewValue = item.NewValue,
                IpAddress = item.IpAddress,
                CreatedAt = DateTime.SpecifyKind(item.CreatedAt, DateTimeKind.Utc)
            }).ToList(),
            TotalCount = total,
            Page = page,
            PageSize = pageSize
        };

        return new ResponseDTO("Thành công.", 200, true, result);
    }
}
