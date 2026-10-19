using System.Net;
using System.Net.Http.Json;
using ApiGateway.DTOs.Restaurant;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Restaurant.DAL.Models;
using Xunit;

namespace Restaurant.Tests;

public sealed partial class GatewayFlowTests
{
    [Fact]
    public async Task ConfiguredLifetimeAppliesToCookieAndStoredAccess()
    {
        var options = _restaurant.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<Restaurant.Common.Settings.GuestAccessSettings>>();
        options.Value.LifetimeHours = 2;
        var session = await CreateGuestSession();
        var before = DateTime.UtcNow;
        var response = await _http.PostAsJsonAsync(
            $"/api/table-sessions/{session.TableSessionId}/guest-access/join", new { phoneOrCode = "123" });
        var result = await Read<ApiGateway.DTOs.Restaurant.GuestAccessDto>(response);
        Assert.InRange(result.ExpiresAt, before.AddHours(2).AddSeconds(-1), DateTime.UtcNow.AddHours(2).AddSeconds(1));
        var cookie = Microsoft.Net.Http.Headers.SetCookieHeaderValue.Parse(response.Headers.GetValues("Set-Cookie").Single());
        Assert.InRange(Math.Abs((cookie.Expires!.Value.UtcDateTime - result.ExpiresAt).TotalSeconds), 0, 1);
        using var scope = _restaurant.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RestaurantDbContext>();
        Assert.Equal(result.ExpiresAt, (await db.GuestAccesses.SingleAsync()).ExpiredAt);
    }

    [Theory]
    [InlineData("0123456789")]
    [InlineData("789")]
    public async Task GuestCanJoinRetryWrongCodeAndReuseCookie(string firstInput)
    {
        var session = await CreateGuestSession();
        var url = $"/api/table-sessions/{session.TableSessionId}/guest-access";
        Assert.Equal(HttpStatusCode.Unauthorized, (await _http.GetAsync(url + "/me")).StatusCode);
        var started = DateTime.UtcNow;
        var response = await _http.PostAsJsonAsync(url + "/join", new { phoneOrCode = firstInput });
        var result = await Read<ApiGateway.DTOs.Restaurant.GuestAccessDto>(response);
        Assert.InRange(result.ExpiresAt, started.AddHours(12).AddSeconds(-1), DateTime.UtcNow.AddHours(12).AddSeconds(1));
        var cookie = response.Headers.GetValues("Set-Cookie").Single();
        Assert.Contains("httponly", cookie.ToLowerInvariant());
        Assert.Contains("samesite=lax", cookie.ToLowerInvariant());
        Assert.DoesNotContain("token", (await response.Content.ReadAsStringAsync()).ToLowerInvariant());

        using var scope = _restaurant.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RestaurantDbContext>();
        var hash = (await db.TableSessions.SingleAsync()).JoinCodeHash;
        Assert.NotEqual(firstInput, hash);
        for (var i = 0; i < 6; i++)
        {
            Assert.Equal(HttpStatusCode.Forbidden,
                (await _http.PostAsJsonAsync(url + "/join", new { phoneOrCode = "000" })).StatusCode);
        }

        Assert.Single(await db.GuestAccesses.ToListAsync());
        await Read<ApiGateway.DTOs.Restaurant.GuestAccessDto>(
            await _http.PostAsJsonAsync(url + "/join", new { phoneOrCode = "789" }));
        db.ChangeTracker.Clear();
        Assert.Equal(hash, (await db.TableSessions.SingleAsync()).JoinCodeHash);
        _http.DefaultRequestHeaders.Add("Cookie", cookie.Split(';')[0]);
        await Read<ApiGateway.DTOs.Restaurant.GuestAccessDto>(await _http.GetAsync(url + "/me"));
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await _http.GetAsync($"/api/table-sessions/{Guid.NewGuid()}/guest-access/me")).StatusCode);
    }

    [Theory]
    [InlineData("expired")]
    [InlineData("revoked")]
    [InlineData("closed")]
    public async Task InvalidGuestAccessCannotBeReused(string reason)
    {
        var session = await CreateGuestSession();
        var url = $"/api/table-sessions/{session.TableSessionId}/guest-access";
        var response = await _http.PostAsJsonAsync(url + "/join", new { phoneOrCode = "123" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        _http.DefaultRequestHeaders.Add("Cookie", response.Headers.GetValues("Set-Cookie").Single().Split(';')[0]);
        using var scope = _restaurant.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RestaurantDbContext>();
        var guest = await db.GuestAccesses.Include(x => x.TableSession).SingleAsync();
        if (reason == "expired")
        {
            guest.ExpiredAt = DateTime.UtcNow.AddMinutes(-1);
        }
        else if (reason == "revoked")
        {
            guest.RevokedAt = DateTime.UtcNow;
        }
        else
        {
            guest.TableSession.ClosedAt = DateTime.UtcNow;
        }

        await db.SaveChangesAsync();
        Assert.Equal(HttpStatusCode.Unauthorized, (await _http.GetAsync(url + "/me")).StatusCode);
        if (reason == "closed")
        {
            Assert.Equal(HttpStatusCode.Conflict,
                (await _http.PostAsJsonAsync(url + "/join", new { phoneOrCode = "123" })).StatusCode);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("12")]
    [InlineData("abc")]
    [InlineData("1234567890")]
    public async Task InvalidJoinInputDoesNotSetCodeOrGrantAccess(string input)
    {
        var session = await CreateGuestSession();
        Assert.Equal(HttpStatusCode.BadRequest, (await _http.PostAsJsonAsync(
            $"/api/table-sessions/{session.TableSessionId}/guest-access/join", new { phoneOrCode = input })).StatusCode);
        using var scope = _restaurant.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RestaurantDbContext>();
        Assert.Null((await db.TableSessions.SingleAsync()).JoinCodeHash);
        Assert.Empty(await db.GuestAccesses.ToListAsync());
    }

    private async Task<TableSessionScanDto> CreateGuestSession()
    {
        var restaurant = await CreateRestaurant();
        var area = await CreateArea(restaurant.RestaurantId, "GUEST");
        var table = await CreateTable(area.AreaId);
        var qr = await Read<QrCreatedDto>(await _http.PostAsync($"/api/qr/tables/{table.TableId}", null), HttpStatusCode.Created);
        _http.DefaultRequestHeaders.Authorization = null;
        return await Read<TableSessionScanDto>(await Scan(qr.Token));
    }
}
