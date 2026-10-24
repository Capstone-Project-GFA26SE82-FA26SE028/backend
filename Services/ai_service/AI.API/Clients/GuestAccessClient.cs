using System.Globalization;
using AI.BLL.Interfaces;
using AI.Common.DTOs;
using AI.Common.Exceptions;
using AI.Common.Settings;
using Backend.Contracts.Restaurant;
using Microsoft.Extensions.Options;

namespace AI.API.Clients;

public sealed class GuestAccessClient(
    GuestAccessGrpc.GuestAccessGrpcClient client,
    IOptions<AISettings> settings,
    TimeProvider clock) : IGuestAccessClient
{
    public async Task<VerifiedGuest> VerifyAsync(GuestContext guest, CancellationToken ct)
    {
        var reply = await client.GetMeAsync(new GuestAccessRequest
        {
            TableSessionId = guest.TableSessionId.ToString(),
            Token = guest.Token
        }, deadline: DateTime.UtcNow.AddSeconds(settings.Value.DependencyTimeoutSeconds), cancellationToken: ct);
        if (!Guid.TryParse(reply.TableSessionId, out var sessionId) || sessionId != guest.TableSessionId
            || !Guid.TryParse(reply.GuestAccessId, out var guestId) || guestId == Guid.Empty
            || !DateTimeOffset.TryParse(reply.ExpiresAt, CultureInfo.InvariantCulture, DateTimeStyles.None, out var expiresAt)
            || expiresAt <= clock.GetUtcNow())
        {
            throw new AIException(401, "QR guest access is invalid or expired.");
        }

        return new VerifiedGuest(sessionId, guestId, expiresAt);
    }
}
