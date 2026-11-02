using Backend.Contracts.Identity;
using Catalog.BLL.Interfaces;
using Catalog.Common.Settings;
using Grpc.Core;
using Microsoft.Extensions.Options;

namespace Catalog.API.Authorization;

public sealed class IdentityCatalogAccess(
    UserGrpc.UserGrpcClient client,
    IHttpContextAccessor httpContextAccessor,
    IOptions<CatalogManagementSettings> settings) : ICatalogAccess
{
    public Task<Guid> RequireMenuManagerAsync(
        CancellationToken cancellationToken)
    {
        return RequireRolesAsync(settings.Value.MenuManagerRoleIds, cancellationToken);
    }

    public Task<Guid> RequireMenuStaffAsync(CancellationToken cancellationToken)
    {
        return RequireRolesAsync(
            settings.Value.MenuManagerRoleIds.Concat(settings.Value.KitchenRoleIds),
            cancellationToken);
    }

    private async Task<Guid> RequireRolesAsync(
        IEnumerable<string> allowedRoles,
        CancellationToken cancellationToken)
    {
        var httpContext = httpContextAccessor.HttpContext;

        var authorization = httpContext?
            .Request.Headers.Authorization.ToString();

        if (string.IsNullOrWhiteSpace(authorization))
        {
            throw new RpcException(new Status(
                StatusCode.Unauthenticated,
                "Yêu cầu xác thực tài khoản."));
        }

        var headers = new Metadata
        {
            { "authorization", authorization }
        };

        var response = await client.GetMeAsync(
            new GetMeRequest(),
            headers,
            deadline: DateTime.UtcNow.AddSeconds(
                settings.Value.IdentityTimeoutSeconds),
            cancellationToken: cancellationToken);

        if (!response.IsSuccess)
        {
            var status = response.StatusCode switch
            {
                401 or 404 => StatusCode.Unauthenticated,
                403 => StatusCode.PermissionDenied,
                _ => StatusCode.Unavailable
            };

            throw new RpcException(new Status(
                status,
                "Không thể xác minh tài khoản."));
        }

        var user = response.Result;

        if (user is null
            || !user.IsActive
            || !Guid.TryParse(user.UserId, out var userId)
            || userId == Guid.Empty)
        {
            throw new RpcException(new Status(
                StatusCode.Unauthenticated,
                "Tài khoản không hợp lệ hoặc đang bị khóa."));
        }

        if (!allowedRoles.Contains(user.RoleId, StringComparer.Ordinal))
        {
            throw new RpcException(new Status(
                StatusCode.PermissionDenied,
                "Bạn không có quyền quản lý thực đơn."));
        }

        return userId;
    }
}
