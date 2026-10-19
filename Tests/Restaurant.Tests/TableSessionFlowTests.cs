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
    private Task<HttpResponseMessage> Scan(string token)
    {
        return _http.PostAsJsonAsync("/api/table-sessions/scan", new ScanTableSessionDto
        {
            Token = token
        });
    }

    [Fact]
    public async Task AnonymousScanCreatesOnceAndReusesSessionWithoutGrantingAccess()
    {
        var restaurant = await CreateRestaurant();
        var area = await CreateArea(restaurant.RestaurantId, "SCAN");
        var table = await CreateTable(area.AreaId);
        var qr = await Read<QrCreatedDto>(await _http.PostAsync($"/api/qr/tables/{table.TableId}", null), HttpStatusCode.Created);
        _http.DefaultRequestHeaders.Authorization = null;

        var response = await Scan(qr.Token);
        Assert.True(response.Headers.CacheControl!.NoStore);
        var first = await Read<TableSessionScanDto>(response);
        Assert.True(first.IsNewSession);
        Assert.Equal(table.TableId, first.TableId);
        Assert.Equal(table.TableCode, first.TableCode);
        Assert.Equal("Open", first.Status);
        var second = await Read<TableSessionScanDto>(await Scan(qr.Token));
        Assert.False(second.IsNewSession);
        Assert.Equal(first.TableSessionId, second.TableSessionId);

        using var scope = _restaurant.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RestaurantDbContext>();
        var session = Assert.Single(await db.TableSessions.ToListAsync());
        var link = Assert.Single(await db.SessionTables.ToListAsync());
        Assert.Equal(session.TableSessionId, link.TableSessionId);
        Assert.Null(link.ReleasedAt);
        Assert.Null(session.OpenedByRef);
        Assert.Null(session.JoinCodeHash);
        Assert.Empty(await db.GuestAccesses.ToListAsync());
        Assert.False(response.Headers.Contains("Set-Cookie"));

        session.ClosedAt = DateTime.UtcNow;
        session.Status = "Closed";
        link.ReleasedAt = session.ClosedAt;
        await db.SaveChangesAsync();
        var next = await Read<TableSessionScanDto>(await Scan(qr.Token));
        Assert.True(next.IsNewSession);
        Assert.NotEqual(first.TableSessionId, next.TableSessionId);
    }

    [Theory]
    [InlineData("invalid")]
    [InlineData("revoked")]
    [InlineData("table")]
    [InlineData("area")]
    public async Task InvalidScanDoesNotCreateSession(string reason)
    {
        var restaurant = await CreateRestaurant();
        var area = await CreateArea(restaurant.RestaurantId, "SCAN");
        var table = await CreateTable(area.AreaId);
        var qr = await Read<QrCreatedDto>(await _http.PostAsync($"/api/qr/tables/{table.TableId}", null), HttpStatusCode.Created);
        using var scope = _restaurant.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RestaurantDbContext>();
        if (reason == "revoked")
        {
            (await db.Qrs.SingleAsync()).RevokedAt = DateTime.UtcNow;
        }
        if (reason == "table")
        {
            (await db.Tables.SingleAsync()).IsActive = false;
        }
        if (reason == "area")
        {
            (await db.Areas.SingleAsync()).IsActive = false;
        }

        await db.SaveChangesAsync();
        _http.DefaultRequestHeaders.Authorization = null;
        Assert.Equal(HttpStatusCode.NotFound, (await Scan(reason == "invalid" ? "bad" : qr.Token)).StatusCode);
        Assert.Empty(await db.TableSessions.ToListAsync());
        Assert.Empty(await db.SessionTables.ToListAsync());
    }

    [Fact]
    public async Task FailedTableAssignmentRollsBackSessionCreation()
    {
        var restaurant = await CreateRestaurant();
        var area = await CreateArea(restaurant.RestaurantId, "SCAN");
        var table = await CreateTable(area.AreaId);
        var qr = await Read<QrCreatedDto>(await _http.PostAsync($"/api/qr/tables/{table.TableId}", null), HttpStatusCode.Created);
        using var scope = _restaurant.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RestaurantDbContext>();
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TRIGGER reject_assignment BEFORE INSERT ON SessionTable
            BEGIN SELECT RAISE(ABORT, 'Simulated assignment failure'); END;
            """);
        Assert.Equal(HttpStatusCode.BadGateway, (await Scan(qr.Token)).StatusCode);
        Assert.Empty(await db.TableSessions.AsNoTracking().ToListAsync());
        Assert.Empty(await db.SessionTables.AsNoTracking().ToListAsync());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ReleasedLinksAreIgnoredAndConflictingOpenSessionsAreRejected(bool released)
    {
        var restaurant = await CreateRestaurant();
        var area = await CreateArea(restaurant.RestaurantId, "SCAN");
        var table = await CreateTable(area.AreaId);
        var qr = await Read<QrCreatedDto>(await _http.PostAsync($"/api/qr/tables/{table.TableId}", null), HttpStatusCode.Created);
        using var scope = _restaurant.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RestaurantDbContext>();
        for (var i = 0; i < 2; i++)
        {
            db.SessionTables.Add(new SessionTable
            {
                SessionTableId = Guid.NewGuid(),
                TableId = table.TableId,
                AssignedAt = DateTime.UtcNow,
                ReleasedAt = released ? DateTime.UtcNow : null,
                TableSession = new TableSession
                {
                    TableSessionId = Guid.NewGuid(),
                    PartySize = 2,
                    Status = "Open",
                    OpenedAt = DateTime.UtcNow
                }
            });
        }

        await db.SaveChangesAsync();
        var response = await Scan(qr.Token);
        if (released)
        {
            Assert.True((await Read<TableSessionScanDto>(response)).IsNewSession);
            Assert.Equal(3, await db.TableSessions.CountAsync());
        }
        else
        {
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Equal(2, await db.TableSessions.CountAsync());
        }
    }
}
