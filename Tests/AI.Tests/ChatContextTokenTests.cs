using AI.API.Clients;
using AI.Common.DTOs;
using AI.Common.Exceptions;
using Microsoft.AspNetCore.DataProtection;

namespace AI.Tests;

public sealed class ChatContextTokenTests
{
    [Fact]
    public void ProtectedContextBindsGuestSessionAndRestaurant()
    {
        var codec = new ChatContextToken(new EphemeralDataProtectionProvider(), TimeProvider.System);
        var guest = new VerifiedGuest(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow.AddHours(1));
        var item = Guid.NewGuid();
        var state = new ChatContextDTO
        {
            PresentedItems = [item],
            ExcludedItems = [Guid.NewGuid()],
            MaximumPrice = 80000,
            Preference = "warm"
        };
        var token = codec.Write(state, guest);

        var decoded = codec.Read(token, guest);
        Assert.Equal(item, Assert.Single(decoded.PresentedItems));
        Assert.Equal(state.ExcludedItems, decoded.ExcludedItems);
        Assert.Equal(80000, decoded.MaximumPrice);
        Assert.Equal(403, Assert.Throws<AIException>(() => codec.Read(token, guest with { GuestAccessId = Guid.NewGuid() })).StatusCode);
        Assert.Equal(403, Assert.Throws<AIException>(() => codec.Read(token, guest with { TableSessionId = Guid.NewGuid() })).StatusCode);
        Assert.Equal(403, Assert.Throws<AIException>(() => codec.Read(token, guest with { RestaurantId = Guid.NewGuid() })).StatusCode);
        Assert.Equal(400, Assert.Throws<AIException>(() => codec.Read("!" + token, guest)).StatusCode);
    }

    [Fact]
    public void ExpiredGuestLimitsTheContextLifetime()
    {
        var codec = new ChatContextToken(new EphemeralDataProtectionProvider(), TimeProvider.System);
        var guest = new VerifiedGuest(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow.AddMinutes(-1));
        var token = codec.Write(new ChatContextDTO(), guest);

        Assert.Equal(400, Assert.Throws<AIException>(() => codec.Read(token, guest)).StatusCode);
    }
}
