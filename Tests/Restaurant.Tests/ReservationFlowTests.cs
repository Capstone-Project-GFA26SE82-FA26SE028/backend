using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ApiGateway.DTOs.Restaurant;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Restaurant.BLL.Interfaces;
using Restaurant.Common.Reservations;
using Restaurant.Common.Settings;
using Restaurant.DAL.Models;
using Xunit;

namespace Restaurant.Tests;

public sealed partial class GatewayFlowTests
{
    private readonly ReservationClock _reservationClock = new();

    private sealed class ReservationClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2030, 1, 7, 8, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow()
        {
            return Now;
        }
    }

    private Task<HttpResponseMessage> Availability(Guid restaurantId, Guid areaId, DateTimeOffset? start = null, int partySize = 2)
    {
        var at = (start ?? _reservationClock.Now.AddHours(1)).ToString("O", CultureInfo.InvariantCulture);
        return _http.GetAsync($"/api/reservations/availability?restaurantId={restaurantId}&areaId={areaId}&partySize={partySize}&startAt={Uri.EscapeDataString(at)}");
    }

    private async Task<Guid> SeedOccupiedSessionForReservation(Guid tableId)
    {
        using var scope = _restaurant.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RestaurantDbContext>();
        var now = DateTime.UtcNow;
        var actor = Guid.NewGuid();
        var session = new TableSession
        {
            TableSessionId = Guid.NewGuid(),
            PartySize = 2,
            Status = "Open",
            OpenedAt = now,
            OpenedByRef = actor
        };
        db.SessionTables.Add(new SessionTable
        {
            SessionTableId = Guid.NewGuid(),
            TableSession = session,
            TableId = tableId,
            AssignedAt = now,
            ChangedByRef = actor
        });
        await db.SaveChangesAsync();
        return session.TableSessionId;
    }

    private async Task<Guid> SeedReservation(string status, DateTimeOffset start, DateTimeOffset? hold = null, params Guid[] tableIds)
    {
        Guid? fallbackAreaId = null;
        if (tableIds.Length == 0)
        {
            fallbackAreaId = (await CreateArea((await CreateRestaurant()).RestaurantId, "RESERVATION")).AreaId;
        }

        using var scope = _restaurant.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RestaurantDbContext>();
        var area = fallbackAreaId.HasValue
            ? await db.Areas.SingleAsync(area => area.AreaId == fallbackAreaId.Value)
            : await db.Tables.Where(table => table.TableId == tableIds[0]).Select(table => table.Area).SingleAsync();
        var reservation = new Reservation
        {
            ReservationId = Guid.NewGuid(),
            RestaurantId = area.RestaurantId,
            RequestedAreaId = area.AreaId,
            ContactName = "Test Guest",
            Phone = "+84901234567",
            PartySize = 2,
            StartAt = start.UtcDateTime,
            EndAt = start.AddHours(2).UtcDateTime,
            HoldUntil = hold?.UtcDateTime,
            Status = status
        };
        foreach (var tableId in tableIds)
        {
            reservation.Tables.Add(await db.Tables.SingleAsync(table => table.TableId == tableId));
        }

        db.Reservations.Add(reservation);
        await db.SaveChangesAsync();
        return reservation.ReservationId;
    }

    [Fact]
    public async Task CreateDoesNotSilentlyDiscardTheRequestedLocation()
    {
        var restaurant = await CreateRestaurant();
        var area = await CreateArea(restaurant.RestaurantId, "BOOK");
        _http.DefaultRequestHeaders.Authorization = null;
        var response = await _http.PostAsJsonAsync("/api/reservations", new
        {
            restaurantId = restaurant.RestaurantId,
            requestedAreaId = area.AreaId,
            contactName = "Test Guest",
            phone = "+84901234567",
            partySize = 2,
            startAt = _reservationClock.Now.AddHours(1).ToString("O")
        });
        var created = await Read<ReservationDto>(response, HttpStatusCode.Created);
        Assert.Equal(restaurant.RestaurantId, created.RestaurantId);
        Assert.Equal(area.AreaId, created.RequestedAreaId);
        Assert.Equal(ReservationStatuses.Pending, created.Status);
        Assert.Null(created.ApprovedByRef);
        Assert.Null(created.AssignedRestaurantId);
        Assert.Null(created.TableSessionId);
        Assert.Empty(created.AssignedTableIds);
        Assert.Equal(TimeSpan.FromMinutes(120), created.EndAt - created.StartAt);
        Assert.Equal(created.StartAt.AddMinutes(30), created.HoldUntil);
        using var scope = _restaurant.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RestaurantDbContext>();
        var stored = await db.Reservations.SingleAsync();
        Assert.Equal(created.ReservationId, stored.ReservationId);
        Assert.Equal(area.AreaId, stored.RequestedAreaId);
        Assert.Equal(restaurant.RestaurantId, stored.RestaurantId);
        Assert.Empty(await db.TableSessions.ToListAsync());
        Assert.Empty(await db.SessionTables.ToListAsync());
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "admin");
        var read = await Read<ReservationDto>(await _http.GetAsync($"/api/reservations/{created.ReservationId}"));
        Assert.Equal(created.RestaurantId, read.RestaurantId);
        var page = await Read<PageResult<ReservationDto>>(await _http.GetAsync(
            $"/api/reservations?restaurantId={restaurant.RestaurantId}&requestedAreaId={area.AreaId}&unassignedOnly=true"));
        Assert.Equal(created.ReservationId, Assert.Single(page.Items).ReservationId);
    }

    [Fact]
    public async Task CustomerCannotProvideAnEndTimeStatusOrExactTable()
    {
        Assert.Equal(HttpStatusCode.BadRequest, (await _http.PostAsJsonAsync("/api/reservations", new
        {
            restaurantId = Guid.NewGuid(),
            requestedAreaId = Guid.NewGuid(),
            contactName = "Test Guest",
            phone = "+84901234567",
            partySize = 2,
            startAt = _reservationClock.Now.AddHours(1).ToString("O"),
            endAt = _reservationClock.Now.AddHours(3).ToString("O"),
            status = "Confirmed",
            tableId = Guid.NewGuid()
        })).StatusCode);
    }

    [Theory]
    [InlineData("other-restaurant", HttpStatusCode.Conflict)]
    [InlineData("inactive", HttpStatusCode.Conflict)]
    [InlineData("missing-area", HttpStatusCode.NotFound)]
    [InlineData("missing-restaurant", HttpStatusCode.NotFound)]
    public async Task CreateRejectsInvalidRequestedLocationWithoutSaving(string scenario, HttpStatusCode expected)
    {
        var restaurant = await CreateRestaurant();
        var areaRestaurantId = scenario == "other-restaurant"
            ? (await CreateRestaurant()).RestaurantId
            : restaurant.RestaurantId;
        var area = await CreateArea(areaRestaurantId, "REQUESTED");
        using var scope = _restaurant.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RestaurantDbContext>();
        if (scenario == "inactive")
        {
            (await db.Areas.SingleAsync(value => value.AreaId == area.AreaId)).IsActive = false;
            await db.SaveChangesAsync();
        }

        _http.DefaultRequestHeaders.Authorization = null;
        var response = await _http.PostAsJsonAsync("/api/reservations", new
        {
            restaurantId = scenario == "missing-restaurant" ? Guid.NewGuid() : restaurant.RestaurantId,
            requestedAreaId = scenario == "missing-area" ? Guid.NewGuid() : area.AreaId,
            contactName = "Test Guest",
            phone = "+84901234567",
            partySize = 4,
            startAt = _reservationClock.Now.AddHours(1).ToString("O")
        });
        Assert.Equal(expected, response.StatusCode);
        Assert.Empty(await db.Reservations.ToListAsync());
    }

    [Fact]
    public async Task PendingLocationFiltersDoNotLeakRequestsFromOtherRestaurants()
    {
        var first = await SeedReservation(ReservationStatuses.Pending, _reservationClock.Now.AddHours(1));
        await SeedReservation(ReservationStatuses.Pending, _reservationClock.Now.AddHours(1));
        using var scope = _restaurant.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RestaurantDbContext>();
        var stored = await db.Reservations.SingleAsync(value => value.ReservationId == first);
        var response = await Read<PageResult<ReservationDto>>(await _http.GetAsync(
            $"/api/reservations?restaurantId={stored.RestaurantId}&unassignedOnly=true"));
        Assert.Equal(first, Assert.Single(response.Items).ReservationId);
        var otherArea = await db.Areas.FirstAsync(value => value.RestaurantId != stored.RestaurantId);
        Assert.Equal(HttpStatusCode.BadRequest, (await _http.GetAsync(
            $"/api/reservations?restaurantId={stored.RestaurantId}&requestedAreaId={otherArea.AreaId}")).StatusCode);
    }

    [Theory]
    [InlineData(0, 2)]
    [InlineData(30, 2)]
    [InlineData(0, 1)]
    public async Task CreateUsesConfiguredDurationAndExplicitOffset(int leadMinutes, int durationHours)
    {
        var restaurant = await CreateRestaurant();
        var area = await CreateArea(restaurant.RestaurantId, "REQUESTED");
        var start = _reservationClock.Now.AddMinutes(30 + leadMinutes);
        using var scope = _restaurant.Services.CreateScope();
        var settings = scope.ServiceProvider.GetRequiredService<IOptions<ReservationSettings>>().Value;
        settings.DurationMinutesByRestaurant[restaurant.RestaurantId] = durationHours * 60;
        _http.DefaultRequestHeaders.Authorization = null;
        var response = await _http.PostAsJsonAsync("/api/reservations", new
        {
            restaurantId = restaurant.RestaurantId,
            requestedAreaId = area.AreaId,
            contactName = " Test Guest ",
            phone = " +84901234567 ",
            partySize = 4,
            startAt = start.ToOffset(TimeSpan.FromHours(7)).ToString("O")
        });
        var result = await Read<ReservationDto>(response, HttpStatusCode.Created);
        Assert.Equal(start, result.StartAt);
        Assert.Equal(start.AddHours(durationHours), result.EndAt);
        Assert.Equal("Test Guest", result.ContactName);
        Assert.Equal("+84901234567", result.Phone);
    }

    [Fact]
    public async Task PendingRemainsUnassignedAndDoesNotAppearUnderAnInventedBranch()
    {
        var restaurant = await CreateRestaurant();
        var id = await SeedReservation(ReservationStatuses.Pending, _reservationClock.Now.AddDays(1));
        var all = await Read<PageResult<ReservationDto>>(await _http.GetAsync("/api/reservations?unassignedOnly=true"));
        var pending = Assert.Single(all.Items);
        Assert.Equal(id, pending.ReservationId);
        Assert.Null(pending.AssignedRestaurantId);
        Assert.Null(pending.AssignedAreaId);
        Assert.False(pending.HasConsistentAssignment);
        var filtered = await Read<PageResult<ReservationDto>>(await _http.GetAsync(
            $"/api/reservations?assignedRestaurantId={restaurant.RestaurantId}"));
        Assert.Empty(filtered.Items);
        Assert.Equal(HttpStatusCode.BadRequest, (await _http.GetAsync(
            $"/api/reservations?assignedRestaurantId={restaurant.RestaurantId}&unassignedOnly=true")).StatusCode);
    }

    [Fact]
    public async Task AssignmentRequiresAllTablesToAgreeOnTheLocation()
    {
        var first = await CreateRestaurant();
        var second = await CreateRestaurant();
        var firstTable = await CreateTable((await CreateArea(first.RestaurantId, "FIRST")).AreaId);
        var secondTable = await CreateTable((await CreateArea(second.RestaurantId, "SECOND")).AreaId);
        var id = await SeedReservation(ReservationStatuses.Confirmed, _reservationClock.Now.AddHours(1),
            tableIds: [firstTable.TableId, secondTable.TableId]);
        var reservation = await Read<ReservationDto>(await _http.GetAsync($"/api/reservations/{id}"));
        Assert.Null(reservation.AssignedRestaurantId);
        Assert.Null(reservation.AssignedAreaId);
        Assert.False(reservation.HasConsistentAssignment);
        var filtered = await Read<PageResult<ReservationDto>>(await _http.GetAsync(
            $"/api/reservations?assignedRestaurantId={first.RestaurantId}"));
        Assert.Empty(filtered.Items);
    }

    [Fact]
    public async Task ConfirmedEndIsPreservedWhenDurationConfigurationChanges()
    {
        var id = await SeedReservation(ReservationStatuses.Confirmed, _reservationClock.Now.AddHours(1));
        using var scope = _restaurant.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<IOptions<ReservationSettings>>().Value.DefaultDurationMinutes = 180;
        var reservation = await Read<ReservationDto>(await _http.GetAsync($"/api/reservations/{id}"));
        Assert.Equal(_reservationClock.Now.AddHours(3), reservation.EndAt);
    }

    [Fact]
    public async Task AvailabilityUses120MinutesAndSupportsBranchOverrides()
    {
        var restaurant = await CreateRestaurant();
        var area = await CreateArea(restaurant.RestaurantId, "BOOK");
        await CreateTable(area.AreaId);
        var preview = await Read<ReservationAvailabilityDto>(await Availability(restaurant.RestaurantId, area.AreaId));
        Assert.Equal(TimeSpan.FromMinutes(120), preview.EndAt - preview.StartAt);
        Assert.Equal(TimeSpan.FromMinutes(30), preview.StartAt - preview.HoldStartsAt);
        Assert.Equal(TimeSpan.FromMinutes(30), preview.HoldUntil - preview.StartAt);
        Assert.True(preview.RequiresStaffLayoutReview);
        using var scope = _restaurant.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<IOptions<ReservationSettings>>().Value
            .DurationMinutesByRestaurant[restaurant.RestaurantId] = 60;
        var updated = await Read<ReservationAvailabilityDto>(await Availability(restaurant.RestaurantId, area.AreaId));
        Assert.Equal(TimeSpan.FromMinutes(60), updated.EndAt - updated.StartAt);
    }

    [Theory]
    [InlineData(29, false)]
    [InlineData(30, true)]
    [InlineData(43200, true)]
    [InlineData(43201, false)]
    public async Task AdvanceBookingBoundariesAreExplicit(int minutes, bool accepted)
    {
        var restaurant = await CreateRestaurant();
        var area = await CreateArea(restaurant.RestaurantId, "BOOK");
        Assert.Equal(accepted ? HttpStatusCode.OK : HttpStatusCode.BadRequest,
            (await Availability(restaurant.RestaurantId, area.AreaId, _reservationClock.Now.AddMinutes(minutes))).StatusCode);
    }

    [Fact]
    public async Task InvalidTimestampsPartySizeAndUnknownResourcesAreRejected()
    {
        var restaurant = await CreateRestaurant();
        var area = await CreateArea(restaurant.RestaurantId, "BOOK");
        Assert.Equal(HttpStatusCode.BadRequest, (await _http.GetAsync(
            $"/api/reservations/availability?restaurantId={restaurant.RestaurantId}&areaId={area.AreaId}&partySize=2&startAt=2030-01-07T09:00:00")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Availability(restaurant.RestaurantId, area.AreaId, partySize: 0)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Availability(Guid.NewGuid(), area.AreaId)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _http.GetAsync($"/api/reservations/{Guid.NewGuid()}")).StatusCode);
    }

    [Theory]
    [InlineData(120, false)]
    [InlineData(150, true)]
    public async Task ConfirmedHoldsUseHalfOpenIntervals(int nextStartMinutes, bool available)
    {
        var restaurant = await CreateRestaurant();
        var area = await CreateArea(restaurant.RestaurantId, "BOOK");
        var table = await CreateTable(area.AreaId);
        var start = _reservationClock.Now.AddHours(1);
        await SeedReservation(ReservationStatuses.Pending, start, tableIds: [table.TableId]);
        Assert.True((await Read<ReservationAvailabilityDto>(await Availability(restaurant.RestaurantId, area.AreaId))).HasEnoughCapacity);
        await SeedReservation(ReservationStatuses.Confirmed, start.AddMinutes(nextStartMinutes), tableIds: [table.TableId]);
        var preview = await Read<ReservationAvailabilityDto>(await Availability(restaurant.RestaurantId, area.AreaId));
        Assert.Equal(available, preview.HasEnoughCapacity);
    }

    [Fact]
    public async Task ConfirmedSchedulesAreRetainedWithoutNoShowProcessing()
    {
        var restaurant = await CreateRestaurant();
        var area = await CreateArea(restaurant.RestaurantId, "BOOK");
        var table = await CreateTable(area.AreaId);
        await SeedReservation(ReservationStatuses.Confirmed, _reservationClock.Now.AddMinutes(-30),
            hold: _reservationClock.Now, tableIds: [table.TableId]);
        Assert.False((await Read<ReservationAvailabilityDto>(await Availability(restaurant.RestaurantId, area.AreaId))).HasEnoughCapacity);
    }

    [Fact]
    public async Task OpenSessionDoesNotAutomaticallyBlockAFutureHold()
    {
        var restaurant = await CreateRestaurant();
        var area = await CreateArea(restaurant.RestaurantId, "BOOK");
        var table = await CreateTable(area.AreaId);
        await SeedOccupiedSessionForReservation(table.TableId);
        var preview = await Read<ReservationAvailabilityDto>(await Availability(restaurant.RestaurantId, area.AreaId));
        Assert.True(preview.HasEnoughCapacity);
        var immediate = await Read<ReservationAvailabilityDto>(await Availability(
            restaurant.RestaurantId, area.AreaId, _reservationClock.Now.AddMinutes(30)));
        Assert.False(immediate.HasEnoughCapacity);
    }

    [Fact]
    public async Task InactiveAreaDoesNotContributeCapacityAndCrossBranchAreaIsRejected()
    {
        var restaurant = await CreateRestaurant();
        var area = await CreateArea(restaurant.RestaurantId, "BOOK");
        await CreateTable(area.AreaId);
        using (var scope = _restaurant.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RestaurantDbContext>();
            (await db.Areas.SingleAsync()).IsActive = false;
            await db.SaveChangesAsync();
        }

        Assert.False((await Read<ReservationAvailabilityDto>(await Availability(restaurant.RestaurantId, area.AreaId))).HasEnoughCapacity);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await Availability((await CreateRestaurant()).RestaurantId, area.AreaId)).StatusCode);
    }

    [Fact]
    public async Task OpeningHoursUseBusinessTimeZoneAndAllowTheExactCloseBoundary()
    {
        var restaurant = await CreateRestaurant();
        var area = await CreateArea(restaurant.RestaurantId, "BOOK");
        await CreateTable(area.AreaId);
        using (var scope = _restaurant.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RestaurantDbContext>();
            (await db.Restaurants.SingleAsync()).TimeZone = "Asia/Ho_Chi_Minh";
            await db.SaveChangesAsync();
        }

        var at = new DateTimeOffset(2030, 1, 7, 13, 0, 0, TimeSpan.Zero);
        Assert.True((await Read<ReservationAvailabilityDto>(await Availability(restaurant.RestaurantId, area.AreaId, at))).HasEnoughCapacity);
        Assert.False((await Read<ReservationAvailabilityDto>(await Availability(restaurant.RestaurantId, area.AreaId, at.AddSeconds(1)))).HasEnoughCapacity);
    }

    [Fact]
    public async Task PendingExpiresOnlyAfterApprovalDeadlineAndKeepsItsHistory()
    {
        var id = await SeedReservation(ReservationStatuses.Pending, _reservationClock.Now.AddMinutes(30));
        using var scope = _restaurant.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IReservationExpirationService>();
        Assert.Equal(0, await service.Expire(CancellationToken.None));
        _reservationClock.Now = _reservationClock.Now.AddSeconds(1);
        Assert.Equal(1, await service.Expire(CancellationToken.None));
        Assert.Equal(0, await service.Expire(CancellationToken.None));
        var db = scope.ServiceProvider.GetRequiredService<RestaurantDbContext>();
        db.ChangeTracker.Clear();
        var stored = await db.Reservations.SingleAsync();
        Assert.Equal(id, stored.ReservationId);
        Assert.Equal(ReservationStatuses.Expired, stored.Status);
        Assert.Null(stored.ApprovedByRef);
        Assert.Empty(await db.TableSessions.ToListAsync());
    }

    [Theory]
    [InlineData("Confirmed")]
    [InlineData("Expired")]
    [InlineData("Cancelled")]
    [InlineData("CheckedIn")]
    public async Task ApprovalExpirationDoesNotProcessOtherStates(string status)
    {
        await SeedReservation(status, _reservationClock.Now.AddHours(-1));
        using var scope = _restaurant.Services.CreateScope();
        Assert.Equal(0, await scope.ServiceProvider.GetRequiredService<IReservationExpirationService>().Expire(CancellationToken.None));
        Assert.Equal(status, (await scope.ServiceProvider.GetRequiredService<RestaurantDbContext>().Reservations.SingleAsync()).Status);
    }

    [Fact]
    public async Task ExistingUniqueReservationSessionConstraintAlsoAppliesAfterSessionClosure()
    {
        var id = await SeedReservation(ReservationStatuses.CheckedIn, _reservationClock.Now.AddHours(-1));
        using var scope = _restaurant.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RestaurantDbContext>();
        db.TableSessions.Add(new TableSession
        {
            TableSessionId = Guid.NewGuid(),
            ReservationId = id,
            PartySize = 2,
            Status = "Closed",
            OpenedAt = _reservationClock.Now.AddHours(-1).UtcDateTime,
            ClosedAt = _reservationClock.Now.UtcDateTime
        });
        await db.SaveChangesAsync();
        db.TableSessions.Add(new TableSession
        {
            TableSessionId = Guid.NewGuid(),
            ReservationId = id,
            PartySize = 2,
            Status = "Open",
            OpenedAt = _reservationClock.Now.UtcDateTime
        });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task MissingDurationIsReportedWithoutInventingAnEndTime()
    {
        var restaurant = await CreateRestaurant();
        var area = await CreateArea(restaurant.RestaurantId, "BOOK");
        using var scope = _restaurant.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<IOptions<ReservationSettings>>().Value.DefaultDurationMinutes = 0;
        var response = await Availability(restaurant.RestaurantId, area.AreaId);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Contains("Cấu hình thời lượng đặt bàn", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task MalformedHoldDoesNotCauseEarlyExpirationOrFreeATable()
    {
        var restaurant = await CreateRestaurant();
        var area = await CreateArea(restaurant.RestaurantId, "BOOK");
        var table = await CreateTable(area.AreaId);
        var id = await SeedReservation(ReservationStatuses.Confirmed, _reservationClock.Now.AddHours(1),
            _reservationClock.Now.AddMinutes(-1), table.TableId);
        using var scope = _restaurant.Services.CreateScope();
        Assert.Equal(0, await scope.ServiceProvider.GetRequiredService<IReservationExpirationService>().Expire(CancellationToken.None));
        var preview = await Read<ReservationAvailabilityDto>(await Availability(restaurant.RestaurantId, area.AreaId));
        Assert.False(preview.HasEnoughCapacity);
        var db = scope.ServiceProvider.GetRequiredService<RestaurantDbContext>();
        Assert.Equal(ReservationStatuses.Confirmed,
            (await db.Reservations.SingleAsync(reservation => reservation.ReservationId == id)).Status);
    }

    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData("revoked", HttpStatusCode.Unauthorized)]
    [InlineData("kitchen", HttpStatusCode.Forbidden)]
    public async Task ReadsRequireAValidStaffOrAdministratorAccount(string? token, HttpStatusCode expected)
    {
        _http.DefaultRequestHeaders.Authorization = token is null ? null : new AuthenticationHeaderValue("Bearer", token);
        Assert.Equal(expected, (await _http.GetAsync("/api/reservations")).StatusCode);
        Assert.Equal(expected, (await _http.GetAsync($"/api/reservations/{Guid.NewGuid()}")).StatusCode);
    }
}
