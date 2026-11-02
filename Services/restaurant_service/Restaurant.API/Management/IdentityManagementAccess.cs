using Backend.Contracts.Identity;
using Grpc.Core;
using Restaurant.BLL.Interfaces;
using Restaurant.Common.Exceptions;

namespace Restaurant.API.Management;

public sealed class IdentityManagementAccess(AccessGrpc.AccessGrpcClient client, IHttpContextAccessor accessor) : IManagementAccess
{
    private Guid? _actor;

    public async Task<Guid> RequireAdmin(CancellationToken ct)
    {
        if (_actor.HasValue)
        {
            return _actor.Value;
        }

        var http = accessor.HttpContext;
        var authorization = http?.Request.Headers.Authorization.ToString();
        if (string.IsNullOrWhiteSpace(authorization))
        {
            throw new ManagementException(401, "Yêu cầu xác thực tài khoản.");
        }

        var headers = new Metadata
        {
            { "authorization", authorization }
        };
        var response = await client.RequireAdminAsync(new GetMeRequest(), headers,
            deadline: DateTime.UtcNow.AddSeconds(5), cancellationToken: ct);
        if (!Guid.TryParse(response.UserId, out var actor))
        {
            throw new ManagementException(503, "Không thể xác minh quyền truy cập.");
        }

        _actor = actor;
        return actor;
    }
}
