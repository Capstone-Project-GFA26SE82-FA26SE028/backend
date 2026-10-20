using System.Globalization;
using Backend.Contracts.Restaurant;
using Grpc.Core;
using Microsoft.Extensions.Options;
using Ordering.BLL.Interfaces;
using Ordering.Common.DTOs;
using Ordering.Common.Exceptions;
using Ordering.Common.Settings;

namespace Ordering.API.Clients;

public sealed class RestaurantOrderingAccess(TableSessionGrpc.TableSessionGrpcClient client,
    IHttpContextAccessor http, IOptions<OrderingSettings> settings, TimeProvider clock) : IOrderingAccess
{
    public async Task<OrderingActor> RequireAsync(Guid sessionId, Guid? tableId, string guestToken, CancellationToken ct)
    {
        if (sessionId == Guid.Empty || tableId == Guid.Empty)
        {
            throw new OrderingException(400, "Invalid session or table ID.");
        }

        var request = new OrderSessionRequest
        {
            TableSessionId = sessionId.ToString(),
            GuestToken = guestToken
        };
        if (tableId.HasValue)
        {
            request.TableId = tableId.Value.ToString();
        }

        var headers = new Metadata();
        var authorization = http.HttpContext?.Request.Headers.Authorization.ToString();
        if (!string.IsNullOrEmpty(authorization))
        {
            headers.Add("authorization", authorization);
        }

        var reply = await client.GetForOrderingAsync(request, headers,
            deadline: DateTime.UtcNow.AddSeconds(settings.Value.DependencyTimeoutSeconds), cancellationToken: ct);
        if (!Guid.TryParse(reply.TableSessionId, out var verified) || verified != sessionId)
        {
            throw new OrderingException(503, "Invalid session verification response.");
        }

        var guest = !string.IsNullOrEmpty(guestToken);
        Guid? guestId = null;
        Guid? userId = null;
        DateTimeOffset? expiry = null;
        if (guest)
        {
            if (!Guid.TryParse(reply.GuestAccessId, out var id) || id == Guid.Empty
                || !DateTimeOffset.TryParse(reply.ExpiresAt, CultureInfo.InvariantCulture, DateTimeStyles.None, out var expires)
                || expires <= clock.GetUtcNow())
            {
                throw new OrderingException(401, "Guest access is invalid or expired.");
            }

            guestId = id;
            expiry = expires;
        }
        else
        {
            if (!Guid.TryParse(reply.UserId, out var id) || id == Guid.Empty)
            {
                throw new OrderingException(401, "Staff access is invalid.");
            }

            userId = id;
        }

        Guid? resolvedTable = null;
        if (reply.HasTableId)
        {
            if (!Guid.TryParse(reply.TableId, out var id) || id == Guid.Empty || (tableId.HasValue && id != tableId))
            {
                throw new OrderingException(503, "Invalid table verification response.");
            }

            resolvedTable = id;
        }

        return new OrderingActor(sessionId, resolvedTable, userId, guestId, expiry);
    }
}
