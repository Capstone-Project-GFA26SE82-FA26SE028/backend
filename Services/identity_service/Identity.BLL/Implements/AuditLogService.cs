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
    public async Task<ResponseDTO> GetAll(Guid actorId, int page, int pageSize, Guid? userId, CancellationToken ct)
    {
        if (!await IsAdmin(actorId, ct))
        {
            return new ResponseDTO("Access denied.", 403);
        }

        var pagination = new PaginationRequest
        {
            Page = page,
            PageSize = pageSize
        };

        if (!pagination.IsValid() || userId == Guid.Empty)
        {
            return new ResponseDTO("Invalid pagination or userId. PageSize must be between 1 and 100.", 400);
        }

        var (items, total) = await repository.GetAllAsync(page, pageSize, userId, ct);
        var result = new PagedResult<AuditLogDTO>
        {
            Items = items.Select(item => new AuditLogDTO
            {
                AuditLogId = item.AuditLogId,
                UserId = item.UserId,
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

        return new ResponseDTO("Success.", 200, true, result);
    }
    
    private async Task<bool> IsAdmin(Guid actorId, CancellationToken ct)
    {
        var actor = await users.FindUserAsync(actorId, ct);
        return actor is { IsActive: true }
            && settings.Value.AdminRoleIds.Contains(actor.RoleId, StringComparer.Ordinal);
    }
}
