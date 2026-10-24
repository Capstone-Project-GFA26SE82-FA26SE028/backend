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
            throw new OrderingException(401, "Authentication is required.");
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
                "Unable to verify the account.");
        }

        if (reply.Result is null || !reply.Result.IsActive
            || !Guid.TryParse(reply.Result.UserId, out var id) || id == Guid.Empty)
        {
            throw new OrderingException(401, "Invalid or inactive account.");
        }

        var allowed = write
            ? roles.Value.WriterRoleIds
            : roles.Value.WriterRoleIds.Concat(roles.Value.ReaderRoleIds);

        if (!allowed.Contains(reply.Result.RoleId, StringComparer.Ordinal))
        {
            throw new OrderingException(403, "You do not have permission to access kitchen stations.");
        }
    }

    public async Task<Guid> RequireKitchenTaskAccessAsync(CancellationToken ct)
    {
        var authorization = http.HttpContext?.Request.Headers.Authorization.ToString();
        if (string.IsNullOrWhiteSpace(authorization))
        {
            throw new OrderingException(401, "Authentication is required.");
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
                "Unable to verify the account.");
        }

        if (reply.Result is null || !reply.Result.IsActive
            || !Guid.TryParse(reply.Result.UserId, out var id) || id == Guid.Empty)
        {
            throw new OrderingException(401, "Invalid or inactive account.");
        }

        var allowed = roles.Value.WriterRoleIds
            .Concat(roles.Value.ReaderRoleIds)
            .Concat(["admin"]);

        if (!allowed.Contains(reply.Result.RoleId, StringComparer.Ordinal))
        {
            throw new OrderingException(403, "You do not have permission to access kitchen tasks.");
        }

        return id;
    }
}
