using System.Net;
using System.Net.Http.Headers;
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
    public async Task AnonymousCustomerCanRequestRestaurantWithNoAreas()
    {
        var restaurant = await CreateRestaurant();
        var pending = await CreateUnassignedReservation(restaurant.RestaurantId);

        Assert.Equal("Pending", pending.Status);
        Assert.Null(pending.RequestedAreaId);
        Assert.Empty(pending.AssignedTableIds);
        using var scope = _restaurant.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RestaurantDbContext>();
        Assert.Empty(await db.Areas.ToListAsync());
        Assert.Null((await db.Reservations.SingleAsync()).RequestedAreaId);
        Assert.True(db.Model.FindEntityType(typeof(Reservation))!
            .FindProperty(nameof(Reservation.RequestedAreaId))!.IsNullable);
    }

    [Fact]
    public async Task WaiterChoosesAreaAtConfirmationAndCheckInStillWorks()
    {
        var restaurant = await CreateRestaurant();
        var pending = await CreateUnassignedReservation(restaurant.RestaurantId);
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "admin");
        var firstArea = await CreateArea(restaurant.RestaurantId, "FIRST");
        var secondArea = await CreateArea(restaurant.RestaurantId, "SECOND");
        var firstTable = await CreateTable(firstArea.AreaId);
        var secondTable = await Read<TableDto>(await _http.PostAsJsonAsync("/api/tables", new TableWriteDto
        {
            AreaId = secondArea.AreaId,
            TableCode = "T2",
            Capacity = 4
        }), HttpStatusCode.Created);
        var outside = await CreateRestaurant();
        var outsideArea = await CreateArea(outside.RestaurantId, "OUTSIDE");
        var outsideTable = await CreateTable(outsideArea.AreaId);
        using (var scope = _restaurant.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RestaurantDbContext>();
            db.RestaurantStaffs.Add(new RestaurantStaff
            {
                RestaurantId = restaurant.RestaurantId,
                UserIdRef = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                IsActive = true
            });
            await db.SaveChangesAsync();
        }

        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "waiter");
        using var foreign = await _http.PostAsJsonAsync($"/api/reservations/{pending.ReservationId}/confirm", new
        {
            TableIds = new[] { outsideTable.TableId }
        });
        Assert.Equal(HttpStatusCode.Conflict, foreign.StatusCode);
        using var mixed = await _http.PostAsJsonAsync($"/api/reservations/{pending.ReservationId}/confirm", new
        {
            TableIds = new[] { firstTable.TableId, secondTable.TableId }
        });
        Assert.Equal(HttpStatusCode.Conflict, mixed.StatusCode);
        var unchanged = await Read<ReservationDto>(await _http.GetAsync($"/api/reservations/{pending.ReservationId}"));
        Assert.Equal("Pending", unchanged.Status);
        Assert.Null(unchanged.RequestedAreaId);
        Assert.Empty(unchanged.AssignedTableIds);

        var confirmed = await Read<ReservationDto>(await _http.PostAsJsonAsync(
            $"/api/reservations/{pending.ReservationId}/confirm", new
            {
                TableIds = new[] { secondTable.TableId }
            }));
        Assert.Equal("Confirmed", confirmed.Status);
        Assert.Equal(secondArea.AreaId, confirmed.RequestedAreaId);
        Assert.Equal(secondArea.AreaId, confirmed.AssignedAreaId);
        Assert.Equal(secondTable.TableId, Assert.Single(confirmed.AssignedTableIds));

        using var duplicate = await _http.PostAsJsonAsync($"/api/reservations/{pending.ReservationId}/confirm", new
        {
            TableIds = new[] { secondTable.TableId }
        });
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        using var tomorrow = await _http.PutAsJsonAsync($"/api/reservations/{pending.ReservationId}", new
        {
            StartAt = pending.StartAt.AddDays(1).ToString("O"),
            TableIds = new[] { secondTable.TableId }
        });
        Assert.Equal(HttpStatusCode.BadRequest, tomorrow.StatusCode);
        Assert.Contains("trong ngày", await tomorrow.Content.ReadAsStringAsync());
        var preserved = await Read<ReservationDto>(await _http.GetAsync($"/api/reservations/{pending.ReservationId}"));
        Assert.Equal(pending.StartAt, preserved.StartAt);
        Assert.Equal(secondArea.AreaId, preserved.AssignedAreaId);

        _reservationClock.Now = pending.StartAt.AddMinutes(-30);
        var arrived = await Read<ReservationDto>(await _http.PostAsync(
            $"/api/reservations/{pending.ReservationId}/check-in", null));
        Assert.Equal("CheckedIn", arrived.Status);
        Assert.Null(arrived.TableSessionId);
    }

    private async Task<ReservationDto> CreateUnassignedReservation(Guid restaurantId)
    {
        _http.DefaultRequestHeaders.Authorization = null;
        return await Read<ReservationDto>(await _http.PostAsJsonAsync("/api/reservations", new
        {
            RestaurantId = restaurantId,
            ContactName = "Khách đặt bàn",
            Phone = "0900000000",
            PartySize = 2,
            StartAt = _reservationClock.Now.AddHours(1).ToString("O")
        }), HttpStatusCode.Created);
    }
}
