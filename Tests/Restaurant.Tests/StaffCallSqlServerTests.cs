using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Restaurant.DAL.Models;
using Shared.Persistence.Notifications;
using Xunit;

namespace Restaurant.Tests;

public sealed partial class ReservationSqlServerTests
{
    private async Task<LocationIds> StaffCallLocation(bool assignStaff = true)
    {
        using var scope = _restaurant.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RestaurantDbContext>();
        var restaurantId = Guid.NewGuid();
        var areaId = Guid.NewGuid();
        var tableId = Guid.NewGuid();
        db.Tables.Add(new Table
        {
            TableId = tableId,
            TableCode = "T1",
            Capacity = 4,
            IsActive = true,
            CleaningStatus = "Clean",
            Area = new Area
            {
                AreaId = areaId,
                AreaCode = "DINING",
                AreaName = "Dining",
                ServiceType = "DineIn",
                SpaceType = "Indoor",
                IsActive = true,
                Restaurant = new Restaurant.DAL.Models.Restaurant
                {
                    RestaurantId = restaurantId,
                    RestaurantName = "Staff call test",
                    Address = "Test address",
                    Hotline = "0901234567",
                    TimeZone = "UTC"
                }
            }
        });
        if (assignStaff)
        {
            db.RestaurantStaffs.Add(new RestaurantStaff
            {
                RestaurantId = restaurantId,
                UserIdRef = _users["waiter"],
                IsActive = true
            });
        }

        await db.SaveChangesAsync();
        return new LocationIds(restaurantId, areaId, tableId);
    }

    [SqlServerFact]
    public async Task ConcurrentStaffCallsAndAcknowledgementsHaveOneWinnerAndOnlyOwnerCompletes()
    {
        var location = await StaffCallLocation();
        await AddSecondPaymentWaiter(location.RestaurantId);
        var session = await SeedOpenPaymentSession(location);
        using var firstGuest = await JoinPaymentSession(session);
        using var secondGuest = await JoinPaymentSession(session);
        var path = $"/api/table-sessions/{session}/staff-calls";
        var created = await ContendForPaymentSession(session,
            () => PostPaymentRequest(firstGuest, path, Guid.NewGuid()),
            () => PostPaymentRequest(secondGuest, path, Guid.NewGuid()));
        Assert.Single(created, response => response.StatusCode == HttpStatusCode.Conflict);
        var request = await Read<StaffCallResult>(Assert.Single(created, response => response.StatusCode == HttpStatusCode.OK));
        var roles = new[] { "waiter", "secondWaiter" };
        var accepted = await ContendForPaymentSession(session,
            () => Send(HttpMethod.Post, path + $"/{request.ServiceRequestId}/acknowledge", roles[0]),
            () => Send(HttpMethod.Post, path + $"/{request.ServiceRequestId}/acknowledge", roles[1]));
        Assert.Single(accepted, response => response.StatusCode == HttpStatusCode.Conflict);
        Assert.Single(accepted, response => response.StatusCode == HttpStatusCode.OK);
        var winner = Array.FindIndex(accepted, response => response.StatusCode == HttpStatusCode.OK);
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(HttpMethod.Post,
            path + $"/{request.ServiceRequestId}/complete", roles[1 - winner])).StatusCode);
        var completed = await Read<StaffCallResult>(await Send(HttpMethod.Post,
            path + $"/{request.ServiceRequestId}/complete", roles[winner]));
        Assert.Equal("Completed", completed.Status);
        Assert.Equal(_users[roles[winner]], completed.HandledByRef);
        Assert.NotNull(completed.CompletedAt);
        using var scope = _restaurant.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RestaurantDbContext>();
        Assert.Equal(1, await db.Set<NotificationOutbox>().CountAsync());
        Assert.Equal("Open", (await db.TableSessions.FindAsync(session))!.Status);
    }

    [SqlServerFact]
    public async Task StaffCallsRejectOtherSessionsAndRestaurantsAndClosedTables()
    {
        var location = await StaffCallLocation();
        var other = await StaffCallLocation(assignStaff: false);
        var session = await SeedOpenPaymentSession(location);
        var otherSession = await SeedOpenPaymentSession(other);
        using var guest = await JoinPaymentSession(session);
        using var otherGuest = await JoinPaymentSession(otherSession);
        var path = $"/api/table-sessions/{session}/staff-calls";
        var otherPath = $"/api/table-sessions/{otherSession}/staff-calls";
        var request = await Read<StaffCallResult>(await PostPaymentRequest(guest, path, Guid.NewGuid()));
        var otherRequest = await Read<StaffCallResult>(await PostPaymentRequest(otherGuest, otherPath, Guid.NewGuid()));
        Assert.Equal(HttpStatusCode.Unauthorized, (await otherGuest.GetAsync(path + "/current")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await otherGuest.GetAsync(otherPath + $"/{request.ServiceRequestId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(HttpMethod.Post,
            otherPath + $"/{otherRequest.ServiceRequestId}/acknowledge", "waiter")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Send(HttpMethod.Get,
            $"/api/restaurants/{location.RestaurantId}/staff-calls/{otherRequest.ServiceRequestId}", "waiter")).StatusCode);
        using (var scope = _restaurant.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RestaurantDbContext>();
            var tableSession = await db.TableSessions.FindAsync(session);
            tableSession!.Status = "Closed";
            tableSession.ClosedAt = _clock.Now.UtcDateTime;
            await db.SaveChangesAsync();
        }

        Assert.Equal(HttpStatusCode.Conflict, (await Send(HttpMethod.Post,
            path + $"/{request.ServiceRequestId}/acknowledge", "waiter")).StatusCode);
        Assert.False((await PostPaymentRequest(guest, path, Guid.NewGuid())).IsSuccessStatusCode);
    }
}
