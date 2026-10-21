using Backend.Contracts.Identity;
using Backend.Contracts.Restaurant;
using Catalog.BLL.Interfaces;
using Catalog.Common.Settings;
using Grpc.Core;
using Microsoft.Extensions.Options;

namespace Catalog.API.Authorization;

public sealed class MenuAccess(
    UserGrpc.UserGrpcClient identity,
    GuestAccessGrpc.GuestAccessGrpcClient guests,
    IHttpContextAccessor httpContextAccessor,
    IOptions<CatalogManagementSettings> settings) : IMenuAccess
{
    public async Task RequireReadAsync(Guid? tableSessionId, string? guestToken, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow.AddSeconds(settings.Value.IdentityTimeoutSeconds);
        if (tableSessionId.HasValue)
        {
            if (tableSessionId == Guid.Empty || guestToken?.Length != 64 || !guestToken.All(Uri.IsHexDigit))
            {
                throw new RpcException(new Status(StatusCode.Unauthenticated, "A valid guest session is required."));
            }

            var guest = await guests.GetMeAsync(new GuestAccessRequest
            {
                TableSessionId = tableSessionId.Value.ToString(),
                Token = guestToken
            }, deadline: deadline, cancellationToken: cancellationToken);
            if (!Guid.TryParse(guest.TableSessionId, out var verifiedSession) || verifiedSession != tableSessionId)
            {
                throw new RpcException(new Status(StatusCode.Unauthenticated, "Invalid guest session."));
            }

            return;
        }

        var authorization = httpContextAccessor.HttpContext?.Request.Headers.Authorization.ToString();
        if (string.IsNullOrWhiteSpace(authorization))
        {
            throw new RpcException(new Status(StatusCode.Unauthenticated, "Authentication is required."));
        }

        var reply = await identity.GetMeAsync(new GetMeRequest(), new Metadata
        {
            { "authorization", authorization }
        }, deadline: deadline, cancellationToken: cancellationToken);
        if (!reply.IsSuccess)
        {
            var status = reply.StatusCode switch
            {
                401 or 404 => StatusCode.Unauthenticated,
                403 => StatusCode.PermissionDenied,
                _ => StatusCode.Unavailable
            };
            throw new RpcException(new Status(status, "Unable to verify menu access."));
        }

        if (reply.Result is null || !reply.Result.IsActive
            || !Guid.TryParse(reply.Result.UserId, out var userId) || userId == Guid.Empty)
        {
            throw new RpcException(new Status(StatusCode.Unauthenticated, "The account is invalid or inactive."));
        }

        if (!settings.Value.MenuReaderRoleIds.Concat(settings.Value.MenuManagerRoleIds)
            .Contains(reply.Result.RoleId, StringComparer.Ordinal))
        {
            throw new RpcException(new Status(StatusCode.PermissionDenied, "You do not have permission to read the menu."));
        }
    }
}
