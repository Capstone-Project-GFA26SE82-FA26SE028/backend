using System.Globalization;
using AI.API.Diagnostics;
using AI.BLL.Interfaces;
using AI.Common.DTOs;
using AI.Common.Exceptions;
using AI.Common.Settings;
using Backend.Contracts.Restaurant;
using Microsoft.Extensions.Options;

namespace AI.API.Clients;

public sealed class GuestAccessClient(
    GuestAccessGrpc.GuestAccessGrpcClient client,
    RestaurantAccessGrpc.RestaurantAccessGrpcClient restaurants,
    IOptions<AISettings> settings,
    TimeProvider clock,
    ILogger<GuestAccessClient> logger) : IGuestAccessClient
{
    public async Task<VerifiedGuest> VerifyAsync(GuestContext guest, CancellationToken ct)
    {
        using var timing = new AssistantTiming(logger, "GuestAccess");
        var deadline = DateTime.UtcNow.AddSeconds(settings.Value.DependencyTimeoutSeconds);
        var reply = await client.GetMeAsync(new GuestAccessRequest
        {
            TableSessionId = guest.TableSessionId.ToString(),
            Token = guest.Token
        }, deadline: deadline, cancellationToken: ct);
        if (!Guid.TryParse(reply.TableSessionId, out var sessionId) || sessionId != guest.TableSessionId
            || !Guid.TryParse(reply.GuestAccessId, out var guestId) || guestId == Guid.Empty
            || !DateTimeOffset.TryParse(reply.ExpiresAt, CultureInfo.InvariantCulture, DateTimeStyles.None, out var expiresAt)
            || expiresAt <= clock.GetUtcNow())
        {
            throw new AIException(401, "QR guest access is invalid or expired.");
        }

        var scope = await restaurants.ResolveDataScopeAsync(new RestaurantDataScopeRequest
        {
            TableSessionId = sessionId.ToString(),
            GuestToken = guest.Token,
            AllowCustomer = true
        }, deadline: deadline, cancellationToken: ct);
        if (!Guid.TryParse(scope.RestaurantId, out var restaurantId) || restaurantId == Guid.Empty)
        {
            throw new AIException(503, "The restaurant for this QR session could not be verified.");
        }

        return new VerifiedGuest(sessionId, guestId, restaurantId, expiresAt);
    }
}
