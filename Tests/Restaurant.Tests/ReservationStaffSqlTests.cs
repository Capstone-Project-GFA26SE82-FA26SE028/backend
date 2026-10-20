using System.Net;
using ApiGateway.DTOs.Restaurant;
using Microsoft.Data.SqlClient;
using Xunit;

namespace Restaurant.Tests;

public sealed partial class ReservationSqlServerTests
{
    [SqlServerFact]
    public async Task BranchScopeProtectsEveryReservationReadAndMutation()
    {
        var assigned = await Location();
        var outside = await Location(false);
        var visible = await Pending(assigned);
        var hidden = await Pending(outside);
        foreach (var role in new[] { "waiter", "cashier" })
        {
            var page = await Read<Restaurant.Common.DTOs.PageResult<ReservationDto>>(await Send(HttpMethod.Get, "/api/reservations", role));
            Assert.Equal(visible.ReservationId, Assert.Single(page.Items).ReservationId);
            var paths = new[]
            {
                $"/api/reservations/{hidden.ReservationId}",
                $"/api/reservations?restaurantId={outside.RestaurantId}",
                $"/api/reservations?requestedAreaId={outside.AreaId}",
                $"/api/reservations?assignedRestaurantId={outside.RestaurantId}",
                $"/api/reservations?assignedAreaId={outside.AreaId}",
                $"/api/reservations/availability?restaurantId={outside.RestaurantId}&areaId={outside.AreaId}&partySize=2&startAt=2030-01-07T10:00:00Z"
            };
            foreach (var path in paths)
            {
                Assert.Equal(HttpStatusCode.Forbidden, (await Send(HttpMethod.Get, path, role)).StatusCode);
            }

            Assert.Equal(HttpStatusCode.Forbidden, (await Confirm(hidden, outside.TableId, role)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await Action(hidden, "reject", role)).StatusCode);
            await AssertMutationsDenied(hidden, outside.TableId, role, HttpStatusCode.Forbidden);
        }

        Assert.Equal("Pending", (await ReadReservation(hidden.ReservationId, "admin")).Status);
    }

    [SqlServerFact]
    public async Task AdminManagesAssignmentsAndRevocationImmediatelyRemovesAccess()
    {
        var location = await Location(false);
        var pending = await Pending(location);
        foreach (var role in new[] { "waiter", "cashier" })
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await Confirm(pending, location.TableId, role)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await Send(HttpMethod.Get, "/api/reservations", role)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await Send(HttpMethod.Post,
                $"/api/restaurants/{location.RestaurantId}/staff", role, new { userIdRef = _users[role] })).StatusCode);
        }

        Assert.Equal(HttpStatusCode.Forbidden, (await Send(HttpMethod.Post,
            $"/api/restaurants/{location.RestaurantId}/staff", "admin", new { userIdRef = _users["kitchen"] })).StatusCode);
        await Grant(location.RestaurantId, "waiter");
        Assert.Equal(HttpStatusCode.Conflict, (await Send(HttpMethod.Post,
            $"/api/restaurants/{location.RestaurantId}/staff", "admin", new { userIdRef = _users["waiter"] })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Confirm(pending, location.TableId, "admin")).StatusCode);
        var confirmed = await Read<ReservationDto>(await Confirm(pending, location.TableId));
        await Read<RestaurantStaffDto>(await Send(HttpMethod.Delete,
            $"/api/restaurants/{location.RestaurantId}/staff/{_users["waiter"]}", "admin"));
        await AssertMutationsDenied(confirmed, location.TableId, "waiter", HttpStatusCode.Forbidden);
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(HttpMethod.Get,
            $"/api/reservations/{pending.ReservationId}", "waiter")).StatusCode);
        var assignments = await Read<RestaurantStaffDto[]>(await Send(HttpMethod.Get,
            $"/api/restaurants/{location.RestaurantId}/staff", "admin"));
        Assert.False(Assert.Single(assignments).IsActive);
        await Grant(location.RestaurantId, "waiter");
        Assert.Equal("Confirmed", (await ReadReservation(confirmed.ReservationId)).Status);
    }

    [SqlServerFact]
    public async Task ConfirmReadsAssignmentAfterConcurrentRevocationCommits()
    {
        var location = await Location();
        var pending = await Pending(location);
        await using var blocker = new SqlConnection(_restaurantConnection);
        await blocker.OpenAsync();
        await using var transaction = (SqlTransaction)await blocker.BeginTransactionAsync();
        await using var command = new SqlCommand("UPDATE dbo.RestaurantStaff SET IsActive = 0 WHERE RestaurantId = @restaurant AND UserIdRef = @user", blocker, transaction);
        command.Parameters.AddWithValue("@restaurant", location.RestaurantId);
        command.Parameters.AddWithValue("@user", _users["waiter"]);
        await command.ExecuteNonQueryAsync();
        var confirm = Confirm(pending, location.TableId);
        var observed = await WaitForBlocked(blocker.ServerProcessId, 1);
        await transaction.CommitAsync();
        Assert.True(observed);
        Assert.Equal(HttpStatusCode.Forbidden, (await confirm).StatusCode);
        Assert.Equal("Pending", (await ReadReservation(pending.ReservationId, "admin")).Status);
    }
}
