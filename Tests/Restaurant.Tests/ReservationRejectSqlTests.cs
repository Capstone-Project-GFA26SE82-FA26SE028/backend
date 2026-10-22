using System.Net;
using ApiGateway.DTOs.Restaurant;
using Identity.Domain.Models;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Restaurant.DAL.Models;
using Xunit;

namespace Restaurant.Tests;

public sealed partial class ReservationSqlServerTests
{
    [SqlServerFact]
    public async Task WaiterAndCashierRejectPendingAtAssignedRestaurants()
    {
        foreach (var role in new[] { "waiter", "cashier" })
        {
            var location = await Location();
            var pending = await Pending(location);
            var rejected = await Read<ReservationDto>(await Action(pending, "reject", role));
            Assert.Equal("Rejected", rejected.Status);
            Assert.Equal(_users[role], rejected.ApprovedByRef);
            Assert.Equal(pending.StartAt, rejected.StartAt);
            Assert.Equal(pending.EndAt, rejected.EndAt);
            Assert.Empty(rejected.AssignedTableIds);
            Assert.Null(rejected.TableSessionId);
            Assert.Equal(HttpStatusCode.Conflict, (await Action(pending, "reject", role)).StatusCode);
            Assert.Equal(HttpStatusCode.Conflict, (await Confirm(pending, location.TableId, role)).StatusCode);
            Assert.Equal("Rejected", (await ReadReservation(pending.ReservationId, role)).Status);
        }

        _clock.Now = _clock.Now.AddDays(1);
        Assert.Equal(0, await RunWorker());
        using var scope = _restaurant.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RestaurantDbContext>();
        Assert.Empty(await db.TableSessions.ToListAsync());
        Assert.Empty(await db.GuestAccesses.ToListAsync());
        Assert.Equal(0, await db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS [Value] FROM ReservationTable").SingleAsync());
    }

    [SqlServerFact]
    public async Task RejectDeadlineIsInclusiveAndLateRequestCommitsExpired()
    {
        var location = await Location();
        var exact = await Pending(location, _clock.Now.AddMinutes(30));
        Assert.Equal("Rejected", (await Read<ReservationDto>(await Action(exact, "reject"))).Status);
        var late = await Pending(location, _clock.Now.AddHours(1));
        _clock.Now = _clock.Now.AddMinutes(30).AddMilliseconds(10);
        Assert.Equal(HttpStatusCode.Conflict, (await Action(late, "reject")).StatusCode);
        var stored = await ReadReservation(late.ReservationId);
        Assert.Equal("Expired", stored.Status);
        Assert.Null(stored.ApprovedByRef);
        Assert.Empty(stored.AssignedTableIds);
        Assert.Equal("Rejected", (await ReadReservation(exact.ReservationId)).Status);
    }

    [SqlServerFact]
    public async Task RejectRequiresCurrentStaffRoleAndValidSession()
    {
        var location = await Location();
        var pending = await Pending(location);
        foreach (var role in new[] { "admin", "kitchen" })
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await Action(pending, "reject", role)).StatusCode);
        }

        using (var scope = _identity.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            foreach (var session in await db.UserSessions.Where(value => value.UserId == _users["waiter"]).ToListAsync())
            {
                session.RevokedAt = DateTime.UtcNow;
            }

            (await db.Users.SingleAsync(value => value.UserId == _users["cashier"])).IsActive = false;
            await db.SaveChangesAsync();
        }

        Assert.Equal(HttpStatusCode.Unauthorized, (await Action(pending, "reject", "waiter")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Action(pending, "reject", "cashier")).StatusCode);
        Assert.Equal("Pending", (await ReadReservation(pending.ReservationId, "admin")).Status);
    }

    [SqlServerFact]
    public async Task ConfirmAndRejectCannotProcessTheSamePendingRequestTwice()
    {
        var location = await Location();
        var pending = await Pending(location);
        await using var blocker = new SqlConnection(_restaurantConnection);
        await blocker.OpenAsync();
        await using var transaction = (SqlTransaction)await blocker.BeginTransactionAsync();
        await LockReservation(blocker, transaction, pending.ReservationId);
        var confirm = Confirm(pending, location.TableId, "waiter");
        var reject = Action(pending, "reject", "cashier");
        var observed = await WaitForBlocked(blocker.ServerProcessId, 2);
        await transaction.CommitAsync();
        var responses = await Task.WhenAll(confirm, reject);
        Assert.True(observed);
        Assert.Single(responses, value => value.StatusCode == HttpStatusCode.OK);
        Assert.Single(responses, value => value.StatusCode == HttpStatusCode.Conflict);
        var stored = await ReadReservation(pending.ReservationId);
        if (stored.Status == "Confirmed")
        {
            Assert.Equal(location.TableId, Assert.Single(stored.AssignedTableIds));
            Assert.Equal(_users["waiter"], stored.ApprovedByRef);
        }
        else
        {
            Assert.Equal("Rejected", stored.Status);
            Assert.Empty(stored.AssignedTableIds);
            Assert.Equal(_users["cashier"], stored.ApprovedByRef);
        }
    }

    [SqlServerFact]
    public async Task RejectAndExpirationAgreeOnExpiredAfterTheDeadline()
    {
        var location = await Location();
        var pending = await Pending(location, _clock.Now.AddMinutes(30));
        _clock.Now = _clock.Now.AddMilliseconds(10);
        await using var blocker = new SqlConnection(_restaurantConnection);
        await blocker.OpenAsync();
        await using var transaction = (SqlTransaction)await blocker.BeginTransactionAsync();
        await LockReservation(blocker, transaction, pending.ReservationId);
        var reject = Action(pending, "reject");
        var worker = RunWorker();
        var observed = await WaitForBlocked(blocker.ServerProcessId, 2);
        await transaction.CommitAsync();
        Assert.Equal(HttpStatusCode.Conflict, (await reject).StatusCode);
        await worker;
        Assert.True(observed);
        var stored = await ReadReservation(pending.ReservationId);
        Assert.Equal("Expired", stored.Status);
        Assert.Null(stored.ApprovedByRef);
        Assert.Empty(stored.AssignedTableIds);
    }

    [SqlServerFact]
    public async Task AssignmentsAreRestoredWithoutCapabilities()
    {
        var response = await _http.GetAsync("/swagger/v1/swagger.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var swagger = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("/api/reservations/capabilities", swagger);
        Assert.Contains("/staff", swagger);
        Assert.DoesNotContain("ReservationCapabilities", swagger);
        using var scope = _restaurant.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RestaurantDbContext>();
        Assert.Equal(1, await db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS [Value] FROM sys.tables WHERE name = 'RestaurantStaff'").SingleAsync());
    }
}
