using Shared.Common.Pagination;
using Shared.Common.DTOs;
using Identity.BLL.Interfaces;
using Identity.Common.DTOs;
using Identity.Common.Settings;
using Identity.DAL.Interfaces;
using Microsoft.Extensions.Options;

namespace Identity.BLL.Implements;

public sealed class SessionService(
    ISessionRepository repository,
    IUserRepository users,
    IOptions<UserManagementSettings> settings) : ISessionService
{
    public async Task<ResponseDTO> GetAll(Guid actorId, int page, int pageSize, Guid? userId, CancellationToken ct, Identity.Common.DTOs.SessionFilter? filter = null)
    {
        if (!await IsAdmin(actorId, ct))
        {
            return new ResponseDTO("Access denied.", 403);
        }

        return await GetPage(page, pageSize, userId, ct, filter);
    }

    public async Task<ResponseDTO> GetMe(Guid userId, int page, int pageSize, CancellationToken ct)
    {
        return await GetPage(page, pageSize, userId, ct);
    }

    public async Task<ResponseDTO> Delete(Guid userId, Guid sessionId, CancellationToken ct)
    {
        if (sessionId == Guid.Empty)
        {
            return new ResponseDTO("Invalid session.", 400);
        }

        var revoked = await repository.RevokeOwnAsync(userId, sessionId, ct);
        return revoked
            ? new ResponseDTO("Session revoked.", 200, true)
            : new ResponseDTO("Session not found.", 404);
    }

    public async Task<ResponseDTO> DeleteAll(Guid userId, CancellationToken ct)
    {
        await repository.RevokeOwnAsync(userId, null, ct);
        return new ResponseDTO("All sessions have been logged out.", 200, true);
    }

    private async Task<ResponseDTO> GetPage(int page, int pageSize, Guid? userId, CancellationToken ct, Identity.Common.DTOs.SessionFilter? filter = null)
    {
        var pagination = new PaginationRequest
        {
            Page = page,
            PageSize = pageSize
        };

        if (!pagination.IsValid() || userId == Guid.Empty || filter?.IsValid() == false)
        {
            return new ResponseDTO("Invalid pagination or userId. PageSize must be between 1 and 100.", 400);
        }

        var (items, total) = await repository.GetAllAsync(page, pageSize, userId, ct, filter);
        var result = new PagedResult<SessionDTO>
        {
            Items = items.Select(item => new SessionDTO
            {
                UserSessionId = item.UserSessionId,
                UserId = item.UserId,
                UserName = item.User.UserName,
                Email = item.User.Email,
                RoleId = item.User.RoleId,
                IpAddress = item.IpAddress,
                UserAgent = item.UserAgent,
                DeviceType = item.DeviceType,
                LoggedInAt = DateTime.SpecifyKind(item.LoggedInAt, DateTimeKind.Utc),
                ExpiresAt = DateTime.SpecifyKind(item.ExpiresAt, DateTimeKind.Utc),
                RevokedAt = item.RevokedAt is { } value ? DateTime.SpecifyKind(value, DateTimeKind.Utc) : null
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
