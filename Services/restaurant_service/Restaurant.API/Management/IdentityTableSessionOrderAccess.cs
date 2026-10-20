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
            throw new ManagementException(401, "Authentication is required.");
        }

        var reply = await client.GetMeAsync(new GetMeRequest(), new Metadata
        {
            { "authorization", authorization }
        }, deadline: DateTime.UtcNow.AddSeconds(5), cancellationToken: cancellationToken);
        if (!reply.IsSuccess)
        {
            throw new ManagementException(reply.StatusCode is 401 or 404 ? 401 : reply.StatusCode == 403 ? 403 : 503,
                "Unable to verify ordering access.");
        }

        var user = reply.Result;
        if (user is null || !user.IsActive || !Guid.TryParse(user.UserId, out var id) || id == Guid.Empty)
        {
            throw new ManagementException(401, "The account is invalid or inactive.");
        }

        var roles = configuration.GetSection("Ordering:StaffRoleIds").Get<string[]>() ?? ["waiter", "manager", "admin"];
        if (!roles.Contains(user.RoleId, StringComparer.Ordinal))
        {
            throw new ManagementException(403, "Ordering staff permission is required.");
        }

        return id;
    }
}
