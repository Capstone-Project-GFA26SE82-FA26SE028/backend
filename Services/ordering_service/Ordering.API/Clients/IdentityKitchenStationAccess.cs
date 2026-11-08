using Backend.Contracts.Identity;
using Grpc.Core;
using Microsoft.Extensions.Options;
using Ordering.BLL.Interfaces;
using Ordering.Common.Exceptions;
using Ordering.Common.Settings;

namespace Ordering.API.Clients;

public sealed class IdentityKitchenStationAccess(
    UserGrpc.UserGrpcClient client,
    IHttpContextAccessor http,
    IOptions<KitchenStationSettings> roles,
    IOptions<OrderingSettings> settings) : IKitchenStationAccess
{
    public async Task RequireAsync(bool write, CancellationToken ct)
    {
        var authorization = http.HttpContext?.Request.Headers.Authorization.ToString();
        if (string.IsNullOrWhiteSpace(authorization))
        {
            throw new OrderingException(401, "Yêu cầu xác thực tài khoản.");
        }

        var headers = new Metadata
        {
            { "authorization", authorization }
        };

        var reply = await client.GetMeAsync(
            new GetMeRequest(),
            headers,
            deadline: DateTime.UtcNow.AddSeconds(settings.Value.DependencyTimeoutSeconds),
            cancellationToken: ct);

        if (!reply.IsSuccess)
        {
            throw new OrderingException(
                reply.StatusCode is 401 or 404 ? 401
                : reply.StatusCode == 403 ? 403 : 503,
                "Không thể xác minh tài khoản.");
        }

        if (reply.Result is null || !reply.Result.IsActive
            || !Guid.TryParse(reply.Result.UserId, out var id) || id == Guid.Empty)
        {
            throw new OrderingException(401, "Tài khoản không hợp lệ hoặc đang bị khóa.");
        }

        var allowed = write
            ? roles.Value.WriterRoleIds
            : roles.Value.WriterRoleIds.Concat(roles.Value.ReaderRoleIds);

        if (!allowed.Contains(reply.Result.RoleId, StringComparer.Ordinal))
        {
            throw new OrderingException(403, "Bạn không có quyền truy cập trạm bếp.");
        }
    }

    public async Task<Guid> RequireKitchenTaskAccessAsync(CancellationToken ct)
    {
        var authorization = http.HttpContext?.Request.Headers.Authorization.ToString();
        if (string.IsNullOrWhiteSpace(authorization))
        {
            throw new OrderingException(401, "Yêu cầu xác thực tài khoản.");
        }

        var headers = new Metadata
        {
            { "authorization", authorization }
        };

        var reply = await client.GetMeAsync(
            new GetMeRequest(),
            headers,
            deadline: DateTime.UtcNow.AddSeconds(settings.Value.DependencyTimeoutSeconds),
            cancellationToken: ct);

        if (!reply.IsSuccess)
        {
            throw new OrderingException(
                reply.StatusCode is 401 or 404 ? 401
                : reply.StatusCode == 403 ? 403 : 503,
                "Không thể xác minh tài khoản.");
        }

        if (reply.Result is null || !reply.Result.IsActive
            || !Guid.TryParse(reply.Result.UserId, out var id) || id == Guid.Empty)
        {
            throw new OrderingException(401, "Tài khoản không hợp lệ hoặc đang bị khóa.");
        }

        var allowed = roles.Value.WriterRoleIds
            .Concat(roles.Value.ReaderRoleIds)
            .Concat(["admin"]);

        if (!allowed.Contains(reply.Result.RoleId, StringComparer.Ordinal))
        {
            throw new OrderingException(403, "Bạn không có quyền truy cập nhiệm vụ bếp.");
        }

        return id;
    }
}
