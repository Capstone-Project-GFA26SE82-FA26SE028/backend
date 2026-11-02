using Backend.Contracts.Identity;
using Grpc.Core;
using Restaurant.BLL.Interfaces;
using Restaurant.Common.Exceptions;

namespace Restaurant.API.Management;

public sealed class IdentityTableSessionOrderAccess(
    UserGrpc.UserGrpcClient client, IHttpContextAccessor accessor, IConfiguration configuration) : ITableSessionOrderAccess
{
    public async Task<Guid> RequireStaffAsync(CancellationToken cancellationToken)
    {
        var authorization = accessor.HttpContext?.Request.Headers.Authorization.ToString();
        if (string.IsNullOrWhiteSpace(authorization))
        {
            throw new ManagementException(401, "Yêu cầu xác thực tài khoản.");
        }

        var reply = await client.GetMeAsync(new GetMeRequest(), new Metadata
        {
            { "authorization", authorization }
        }, deadline: DateTime.UtcNow.AddSeconds(5), cancellationToken: cancellationToken);
        if (!reply.IsSuccess)
        {
            throw new ManagementException(reply.StatusCode is 401 or 404 ? 401 : reply.StatusCode == 403 ? 403 : 503,
                "Không thể xác minh quyền đặt món.");
        }

        var user = reply.Result;
        if (user is null || !user.IsActive || !Guid.TryParse(user.UserId, out var id) || id == Guid.Empty)
        {
            throw new ManagementException(401, "Tài khoản không hợp lệ hoặc đang bị khóa.");
        }

        var roles = configuration.GetSection("Ordering:StaffRoleIds").Get<string[]>() ?? ["waiter"];
        if (!roles.Contains(user.RoleId, StringComparer.Ordinal))
        {
            throw new ManagementException(403, "Yêu cầu quyền nhân viên phục vụ/gọi món.");
        }

        return id;
    }
}
