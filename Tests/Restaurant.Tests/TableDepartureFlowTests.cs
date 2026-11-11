using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ApiGateway.DTOs.Restaurant;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Restaurant.BLL.Interfaces;
using Restaurant.Common.Exceptions;
using Restaurant.DAL.Models;
using Xunit;

namespace Restaurant.Tests;

public sealed partial class GatewayFlowTests
{
    private readonly DeparturePayment _departurePayment = new();

    [Fact]
    public async Task PaidDepartureRevokesGuestsRequiresCleaningAndReplayCannotAffectNextGuest()
    {
        var (sessionId, restaurantId) = await SeedPaymentSession();
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "waiter");
        Guid tableId;
        using (var scope = _restaurant.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RestaurantDbContext>();
            tableId = (await db.SessionTables.SingleAsync()).TableId;
            db.GuestAccesses.Add(new GuestAccess
            {
                GuestAccessId = Guid.NewGuid(),
                TableSessionId = sessionId,
                TokenHash = new string('a', 64),
                CreatedAt = DateTime.UtcNow.AddMinutes(-5),
                ExpiredAt = DateTime.UtcNow.AddHours(1)
            });
            await db.SaveChangesAsync();
        }

        var result = await Read<TableSessionDepartureDto>(await _http.PostAsync($"/api/table-sessions/{sessionId}/depart", null));
        Assert.Equal("Closed", result.Status);
        Assert.Equal(restaurantId, result.RestaurantId);
        Assert.Equal(tableId, Assert.Single(result.TableIds));
        Assert.Equal((sessionId, restaurantId), Assert.Single(_departurePayment.Requests));
        using (var scope = _restaurant.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RestaurantDbContext>();
            var session = await db.TableSessions.SingleAsync();
            var link = await db.SessionTables.SingleAsync();
            Assert.Equal(result.ClosedAt.UtcDateTime, session.ClosedAt);
            Assert.Equal(session.ClosedAt, link.ReleasedAt);
            Assert.NotNull(link.ChangedByRef);
            Assert.Equal(session.ClosedAt, (await db.GuestAccesses.SingleAsync()).RevokedAt);
            Assert.Equal("Dirty", (await db.Tables.SingleAsync()).CleaningStatus);
        }

        Assert.Equal(HttpStatusCode.Conflict, (await _http.PostAsync($"/api/table-sessions/tables/{tableId}/open", null)).StatusCode);
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "admin");
        var qr = await Read<QrCreatedDto>(await _http.PostAsync($"/api/qr/tables/{tableId}", null), HttpStatusCode.Created);
        _http.DefaultRequestHeaders.Authorization = null;
        Assert.Equal(HttpStatusCode.Conflict, (await Scan(qr.Token)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await _http.PostAsJsonAsync($"/api/table-sessions/{sessionId}/guest-access/join",
            new { phoneOrCode = "929" })).StatusCode);
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "waiter");
        var tables = await Read<PageResult<ServiceTableDto>>(await _http.GetAsync($"/api/tables/service?restaurantId={restaurantId}"));
        var table = Assert.Single(tables.Items);
        Assert.Equal("Dirty", table.CleaningStatus);
        Assert.NotEmpty(table.Version);
        var clean = await Read<TableDto>(await _http.PostAsJsonAsync($"/api/tables/{tableId}/confirm-clean", new { version = table.Version }));
        Assert.Equal("Clean", clean.CleaningStatus);
        Assert.Equal(HttpStatusCode.Conflict, (await _http.PostAsJsonAsync($"/api/tables/{tableId}/confirm-clean", new { version = table.Version })).StatusCode);
        var next = await Read<TableSessionScanDto>(await _http.PostAsync($"/api/table-sessions/tables/{tableId}/open", null));
        Assert.NotEqual(sessionId, next.TableSessionId);
        var repeated = await Read<TableSessionDepartureDto>(await _http.PostAsync($"/api/table-sessions/{sessionId}/depart", null));
        Assert.Equal(result.ClosedAt, repeated.ClosedAt);
        Assert.Single(_departurePayment.Requests);
        using var verification = _restaurant.Services.CreateScope();
        var context = verification.ServiceProvider.GetRequiredService<RestaurantDbContext>();
        Assert.Equal("Clean", (await context.Tables.SingleAsync()).CleaningStatus);
        Assert.Equal("Open", (await context.TableSessions.SingleAsync(value => value.TableSessionId == next.TableSessionId)).Status);
        Assert.Null((await context.SessionTables.SingleAsync(value => value.TableSessionId == next.TableSessionId)).ReleasedAt);
    }

    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData("cashier", HttpStatusCode.Forbidden)]
    [InlineData("manager", HttpStatusCode.Forbidden)]
    [InlineData("admin", HttpStatusCode.Forbidden)]
    [InlineData("revoked", HttpStatusCode.Unauthorized)]
    public async Task DepartureAndCleaningRejectOtherRoles(string? role, HttpStatusCode expected)
    {
        var (sessionId, _) = await SeedPaymentSession();
        using var scope = _restaurant.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RestaurantDbContext>();
        var table = await db.Tables.SingleAsync();
        _http.DefaultRequestHeaders.Authorization = role is null ? null : new AuthenticationHeaderValue("Bearer", role);
        Assert.Equal(expected, (await _http.PostAsync($"/api/table-sessions/{sessionId}/depart", null)).StatusCode);
        Assert.Equal(expected, (await _http.PostAsJsonAsync($"/api/tables/{table.TableId}/confirm-clean",
            new { version = Convert.ToBase64String(table.Version) })).StatusCode);
        Assert.Empty(_departurePayment.Requests);
        Assert.Equal("Open", (await db.TableSessions.AsNoTracking().SingleAsync()).Status);
    }

    [Theory]
    [InlineData(409)]
    [InlineData(503)]
    public async Task MissingPaymentOrBillingOutageLeavesSessionAndGuestAccessUnchanged(int code)
    {
        var (sessionId, _) = await SeedPaymentSession();
        _departurePayment.Error = code;
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "waiter");
        Assert.Equal((HttpStatusCode)code, (await _http.PostAsync($"/api/table-sessions/{sessionId}/depart", null)).StatusCode);
        using var scope = _restaurant.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RestaurantDbContext>();
        Assert.Null((await db.TableSessions.SingleAsync()).ClosedAt);
        Assert.Null((await db.SessionTables.SingleAsync()).ReleasedAt);
        Assert.Equal("Clean", (await db.Tables.SingleAsync()).CleaningStatus);
    }

    [Fact]
    public async Task RevokedRestaurantAssignmentCannotDepartOrClean()
    {
        var (sessionId, _) = await SeedPaymentSession();
        using var scope = _restaurant.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RestaurantDbContext>();
        (await db.RestaurantStaffs.SingleAsync()).IsActive = false;
        await db.SaveChangesAsync();
        var table = await db.Tables.SingleAsync();
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "waiter");
        Assert.Equal(HttpStatusCode.Forbidden, (await _http.PostAsync($"/api/table-sessions/{sessionId}/depart", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _http.PostAsJsonAsync($"/api/tables/{table.TableId}/confirm-clean",
            new { version = Convert.ToBase64String(table.Version) })).StatusCode);
        Assert.Empty(_departurePayment.Requests);
    }

    [Fact]
    public async Task CleaningCannotReleaseAnOccupiedTableAndFailedDepartureRollsBack()
    {
        var (sessionId, _) = await SeedPaymentSession();
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "waiter");
        using var scope = _restaurant.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RestaurantDbContext>();
        var table = await db.Tables.SingleAsync();
        Assert.Equal(HttpStatusCode.Conflict, (await _http.PostAsJsonAsync($"/api/tables/{table.TableId}/confirm-clean",
            new { version = Convert.ToBase64String(table.Version) })).StatusCode);
        await db.Database.ExecuteSqlRawAsync("CREATE TRIGGER reject_departure BEFORE UPDATE ON TableSession BEGIN SELECT RAISE(ABORT, 'Simulated close failure'); END;");
        Assert.Equal(HttpStatusCode.BadGateway, (await _http.PostAsync($"/api/table-sessions/{sessionId}/depart", null)).StatusCode);
        Assert.Null((await db.TableSessions.AsNoTracking().SingleAsync()).ClosedAt);
        Assert.Null((await db.SessionTables.AsNoTracking().SingleAsync()).ReleasedAt);
        Assert.Equal("Clean", (await db.Tables.AsNoTracking().SingleAsync()).CleaningStatus);
    }

    private sealed class DeparturePayment : ISessionPaymentClient
    {
        public int? Error { get; set; }
        public List<(Guid SessionId, Guid RestaurantId)> Requests { get; } = [];

        public Task RequirePaidAsync(Guid sessionId, Guid restaurantId, CancellationToken ct)
        {
            Requests.Add((sessionId, restaurantId));
            if (Error.HasValue)
            {
                throw new ManagementException(Error.Value, "Payment not verified.");
            }

            return Task.CompletedTask;
        }
    }
}
