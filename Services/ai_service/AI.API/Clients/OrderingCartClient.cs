using System.Globalization;
using AI.BLL.Interfaces;
using AI.Common.DTOs;
using AI.Common.Exceptions;
using AI.Common.Settings;
using Backend.Contracts.Ordering;
using Microsoft.Extensions.Options;

namespace AI.API.Clients;

public sealed class OrderingCartClient(CartGrpc.CartGrpcClient client, IOptions<AISettings> settings) : ICartClient
{
    public async Task<CartContextDTO> GetByIdAsync(GuestContext guest, Guid cartId, CancellationToken ct)
    {
        var reply = await client.GetByIdAsync(new CartIdRequest
        {
            Access = new OrderingAccess
            {
                TableSessionId = guest.TableSessionId.ToString(),
                GuestToken = guest.Token
            },
            CartId = cartId.ToString()
        }, deadline: DateTime.UtcNow.AddSeconds(settings.Value.DependencyTimeoutSeconds), cancellationToken: ct);
        if (!Guid.TryParse(reply.CartId, out var returnedCartId) || returnedCartId != cartId
            || !Guid.TryParse(reply.TableSessionId, out var sessionId) || sessionId != guest.TableSessionId
            || !DateTimeOffset.TryParse(reply.ExpiresAt, CultureInfo.InvariantCulture, DateTimeStyles.None, out var expiresAt)
            || string.IsNullOrEmpty(reply.Version))
        {
            throw new AIException(503, "The cart response does not match the requested session and cart.");
        }

        var lines = new List<CartLineDTO>();
        foreach (var item in reply.Items)
        {
            if (!Guid.TryParse(item.ItemId, out var itemId) || itemId == Guid.Empty || item.Quantity <= 0)
            {
                throw new AIException(503, "The cart contains invalid item information.");
            }

            lines.Add(new CartLineDTO(itemId, item.Quantity));
        }

        return new CartContextDTO(returnedCartId, sessionId, expiresAt, reply.Version, lines);
    }
}
