using System.Security.Cryptography;
using System.Text.Json;
using AI.BLL.Interfaces;
using AI.Common.DTOs;
using AI.Common.Exceptions;
using Microsoft.AspNetCore.DataProtection;

namespace AI.API.Clients;

public sealed class ChatContextToken(IDataProtectionProvider provider, TimeProvider clock) : IChatContextToken
{
    private const int MaximumLength = 4096;
    private readonly ITimeLimitedDataProtector protector = provider
        .CreateProtector("RestaurantAssistant.ChatContext.v1").ToTimeLimitedDataProtector();

    public ChatContextDTO Read(string token, VerifiedGuest guest)
    {
        Envelope? envelope;
        try
        {
            if (token.Length > MaximumLength)
            {
                throw new AIException(400, "The conversation context is too long.");
            }

            envelope = JsonSerializer.Deserialize<Envelope>(protector.Unprotect(token));
        }
        catch (Exception error) when (error is CryptographicException or JsonException or FormatException)
        {
            throw new AIException(400, "The conversation context is invalid or expired. Start a new chat.");
        }

        if (envelope is null || envelope.SessionId != guest.TableSessionId || envelope.GuestId != guest.GuestAccessId
            || envelope.RestaurantId != guest.RestaurantId)
        {
            throw new AIException(403, "The conversation context does not belong to this guest session.");
        }

        return envelope.Context;
    }

    public string Write(ChatContextDTO context, VerifiedGuest guest)
    {
        var maximum = clock.GetUtcNow().AddMinutes(30);
        var until = guest.ExpiresAt < maximum ? guest.ExpiresAt : maximum;
        var data = new Envelope(guest.TableSessionId, guest.GuestAccessId, guest.RestaurantId, context);
        return protector.Protect(JsonSerializer.Serialize(data), until);
    }

    private sealed record Envelope(Guid SessionId, Guid GuestId, Guid RestaurantId, ChatContextDTO Context);
}
