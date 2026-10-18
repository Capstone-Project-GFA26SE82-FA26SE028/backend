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
    public async Task<Guid> RequireMenuManagerAsync(
        CancellationToken cancellationToken)
    {
        var httpContext = httpContextAccessor.HttpContext;

        var authorization = httpContext?
            .Request.Headers.Authorization.ToString();

        if (string.IsNullOrWhiteSpace(authorization))
        {
            throw new RpcException(new Status(
                StatusCode.Unauthenticated,
                "Authentication is required."));
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
                "Unable to verify the account."));
        }

        var user = response.Result;

        if (user is null
            || !user.IsActive
            || !Guid.TryParse(user.UserId, out var userId)
            || userId == Guid.Empty)
        {
            throw new RpcException(new Status(
                StatusCode.Unauthenticated,
                "The account is invalid or inactive."));
        }

        var allowedRoles = settings.Value.MenuManagerRoleIds;

        if (!allowedRoles.Contains(user.RoleId, StringComparer.Ordinal))
        {
            throw new RpcException(new Status(
                StatusCode.PermissionDenied,
                "You do not have permission to manage the shared menu."));
        }

        return userId;
    }
}
