using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ApiGateway.DTOs.Restaurant;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Restaurant.DAL.Models;
using Xunit;

namespace Restaurant.Tests;

public sealed partial class GatewayFlowTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("https://example.test/restaurant-logo.png")]
    public async Task AnonymousScanReturnsMetadataOnlyForTheVerifiedQrRestaurant(string? logo)
    {
        var other = await CreateRestaurant();
        var selected = await CreateRestaurant();
        var otherArea = await CreateArea(other.RestaurantId, "OTHER");
        var selectedArea = await CreateArea(selected.RestaurantId, "DINING");
        var table = await CreateTable(selectedArea.AreaId);
        var qr = await Read<QrCreatedDto>(await _http.PostAsync(
            $"/api/qr/tables/{table.TableId}", null), HttpStatusCode.Created);
        using (var scope = _restaurant.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RestaurantDbContext>();
            var actual = await db.Restaurants.SingleAsync(item => item.RestaurantId == selected.RestaurantId);
            actual.RestaurantName = "Nhà hàng được quét";
            actual.LogoUrl = logo!;
            var foreign = await db.Restaurants.SingleAsync(item => item.RestaurantId == other.RestaurantId);
            foreign.RestaurantName = "Nhà hàng khác";
            foreign.LogoUrl = "https://example.test/foreign-logo.png";
            var area = await db.Areas.SingleAsync(item => item.AreaId == selectedArea.AreaId);
            area.AreaName = "Khu vực sân vườn";
            await db.SaveChangesAsync();
        }

        _http.DefaultRequestHeaders.Authorization = null;
        var response = await _http.PostAsJsonAsync("/api/table-sessions/scan", new
        {
            token = qr.Token,
            restaurantId = other.RestaurantId,
            areaId = otherArea.AreaId,
            restaurantName = "Client supplied name",
            logoUrl = "https://example.test/client-logo.png"
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl!.NoStore);
        Assert.False(response.Headers.Contains("Set-Cookie"));
        var scan = await Read<TableSessionScanDto>(response);
        Assert.True(scan.IsNewSession);
        Assert.Equal(table.TableId, scan.TableId);
        Assert.Equal(table.TableCode, scan.TableCode);
        Assert.Equal(selected.RestaurantId, scan.RestaurantId);
        Assert.Equal(selectedArea.AreaId, scan.AreaId);
        Assert.Equal("Nhà hàng được quét", scan.RestaurantName);
        Assert.Equal("Khu vực sân vườn", scan.AreaName);
        Assert.Equal(string.IsNullOrWhiteSpace(logo) ? null : logo, scan.LogoUrl);
        Assert.Equal("Open", scan.Status);
        Assert.NotEqual(default, scan.OpenedAt);

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var wire = json.RootElement.GetProperty("result");
        Assert.True(wire.TryGetProperty("logoUrl", out var wireLogo));
        Assert.Equal(string.IsNullOrWhiteSpace(logo) ? JsonValueKind.Null : JsonValueKind.String, wireLogo.ValueKind);
        Assert.Equal("Nhà hàng được quét", wire.GetProperty("restaurantName").GetString());
        Assert.Equal("Khu vực sân vườn", wire.GetProperty("areaName").GetString());

        var reused = await Read<TableSessionScanDto>(await Scan(qr.Token));
        Assert.False(reused.IsNewSession);
        Assert.Equal(scan.TableSessionId, reused.TableSessionId);
        Assert.Equal(scan.RestaurantName, reused.RestaurantName);
        Assert.Equal(scan.AreaName, reused.AreaName);
        Assert.Equal(scan.LogoUrl, reused.LogoUrl);
        using var verification = _restaurant.Services.CreateScope();
        var verifyDb = verification.ServiceProvider.GetRequiredService<RestaurantDbContext>();
        Assert.Single(await verifyDb.TableSessions.ToListAsync());
        Assert.Empty(await verifyDb.GuestAccesses.ToListAsync());
    }

    [Fact]
    public async Task ReusedSessionReturnsCurrentRestaurantAndAreaMetadata()
    {
        var restaurant = await CreateRestaurant();
        var area = await CreateArea(restaurant.RestaurantId, "CURRENT");
        var table = await CreateTable(area.AreaId);
        var qr = await Read<QrCreatedDto>(await _http.PostAsync(
            $"/api/qr/tables/{table.TableId}", null), HttpStatusCode.Created);
        _http.DefaultRequestHeaders.Authorization = null;
        var initial = await Read<TableSessionScanDto>(await Scan(qr.Token));
        using (var scope = _restaurant.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RestaurantDbContext>();
            var changedRestaurant = await db.Restaurants.SingleAsync();
            changedRestaurant.RestaurantName = "Updated restaurant";
            changedRestaurant.LogoUrl = "https://example.test/updated-logo.png";
            var changedArea = await db.Areas.SingleAsync();
            changedArea.AreaName = "Updated area";
            await db.SaveChangesAsync();
        }

        var updated = await Read<TableSessionScanDto>(await Scan(qr.Token));
        Assert.Equal(initial.TableSessionId, updated.TableSessionId);
        Assert.False(updated.IsNewSession);
        Assert.Equal("Updated restaurant", updated.RestaurantName);
        Assert.Equal("Updated area", updated.AreaName);
        Assert.Equal("https://example.test/updated-logo.png", updated.LogoUrl);
    }
}
