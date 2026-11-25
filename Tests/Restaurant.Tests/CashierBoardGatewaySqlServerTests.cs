using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Restaurant.DAL.Models;
using Xunit;
using RestaurantEntity = Restaurant.DAL.Models.Restaurant;

namespace Restaurant.Tests;

public sealed partial class ReservationSqlServerTests
{
    private async Task<LocationIds> BillingLocation(bool assignStaff = true)
    {
        var location = new LocationIds(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        using (var scope = _restaurant.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RestaurantDbContext>();
            db.Restaurants.Add(new RestaurantEntity
            {
                RestaurantId = location.RestaurantId,
                RestaurantName = "Cashier board test",
                Address = "Synthetic test address",
                Hotline = "0901234567",
                TimeZone = "UTC"
            });
            foreach (var day in Enumerable.Range(0, 7))
            {
                db.OpeningHours.Add(new OpeningHour
                {
                    OpeningHourId = Guid.NewGuid(),
                    RestaurantId = location.RestaurantId,
                    DayOfWeek = day,
                    OpenAt = new TimeOnly(0, 0),
                    CloseAt = new TimeOnly(23, 59)
                });
            }

            db.Areas.Add(new Area
            {
                AreaId = location.AreaId,
                RestaurantId = location.RestaurantId,
                AreaCode = "DINING",
                AreaName = "Dining Room",
                ServiceType = "DineIn",
                SpaceType = "Indoor",
                IsActive = true
            });
            db.Tables.Add(new Table
            {
                TableId = location.TableId,
                AreaId = location.AreaId,
                TableCode = "T1",
                Capacity = 4,
                IsActive = true,
                CleaningStatus = "Clean"
            });
            await db.SaveChangesAsync();
        }

        if (assignStaff)
        {
            await Grant(location.RestaurantId, "waiter");
            await Grant(location.RestaurantId, "cashier");
        }

        return location;
    }

    private async Task VerifyCashierBoardFilters(HttpClient cashier, HttpClient waiter, LocationIds location,
        Guid multipleTableSession, Guid unpaidBill, Guid shiftId)
    {
        var incoming = $"/api/restaurants/{location.RestaurantId}/payment-requests/incoming";
        var paid = $"/api/restaurants/{location.RestaurantId}/payment-requests/paid";
        var emptyPaid = await Read<CashierBoardPage>(await cashier.GetAsync(paid));
        Assert.Empty(emptyPaid.Items);
        Assert.Equal(0, emptyPaid.TotalCount);
        Assert.Equal(shiftId, emptyPaid.CashShiftId);

        var firstPage = await Read<CashierBoardPage>(await cashier.GetAsync(incoming + "?pageSize=1"));
        Assert.Single(firstPage.Items);
        Assert.Equal(3, firstPage.TotalCount);
        Assert.Equal(100m, firstPage.KnownTotalAmount);
        Assert.Null(firstPage.TotalAmount);
        Assert.Equal(2, firstPage.UnbilledCount);

        var billMatch = await Read<CashierBoardPage>(await cashier.GetAsync(incoming + $"?search={unpaidBill:N}&pageSize=1"));
        var billCard = Assert.Single(billMatch.Items);
        Assert.Equal(unpaidBill, billCard.BillId);
        Assert.Equal(unpaidBill.ToString("N"), billCard.BillCode);
        Assert.Equal("T3", billCard.TableCode);
        Assert.Equal(100m, billMatch.TotalAmount);
        Assert.Equal(1, billMatch.TotalCount);
        Assert.Equal(0, billMatch.UnbilledCount);
        var beyondPage = await Read<CashierBoardPage>(await cashier.GetAsync(incoming + $"?search={unpaidBill:N}&page=2&pageSize=1"));
        Assert.Empty(beyondPage.Items);
        Assert.Equal(1, beyondPage.TotalCount);
        Assert.Equal(100m, beyondPage.KnownTotalAmount);

        var tableMatch = await Read<CashierBoardPage>(await cashier.GetAsync(incoming + "?search=t2&pageSize=1"));
        Assert.Equal(multipleTableSession, Assert.Single(tableMatch.Items).TableSessionId);
        Assert.Equal(1, tableMatch.TotalCount);

        var extraAreaId = Guid.NewGuid();
        var extraTableId = Guid.NewGuid();
        using (var scope = _restaurant.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RestaurantDbContext>();
            db.Areas.Add(new Area
            {
                AreaId = extraAreaId,
                RestaurantId = location.RestaurantId,
                AreaCode = "BOARD",
                AreaName = "Board extra area",
                ServiceType = "DineIn",
                SpaceType = "Indoor",
                IsActive = true
            });
            db.Tables.Add(new Table
            {
                TableId = extraTableId,
                AreaId = extraAreaId,
                TableCode = "BOARD-EXTRA",
                Capacity = 4,
                IsActive = true,
                CleaningStatus = "Clean"
            });
            db.SessionTables.Add(new SessionTable
            {
                SessionTableId = Guid.NewGuid(),
                TableSessionId = multipleTableSession,
                TableId = extraTableId,
                AssignedAt = _clock.Now.UtcDateTime
            });
            await db.SaveChangesAsync();
        }

        var areaMatch = await Read<CashierBoardPage>(await cashier.GetAsync(incoming + $"?areaId={extraAreaId}&pageSize=1"));
        var multiple = Assert.Single(areaMatch.Items);
        Assert.Equal(multipleTableSession, multiple.TableSessionId);
        Assert.Equal(2, multiple.Tables.Length);
        Assert.Null(multiple.TableId);
        Assert.Null(multiple.TableCode);
        Assert.Null(multiple.AreaId);
        Assert.Null(multiple.AreaName);
        Assert.Contains(multiple.Tables, table => table.TableId == extraTableId && table.AreaId == extraAreaId);
        Assert.Equal(1, areaMatch.TotalCount);
        var differentAreaSearch = await Read<CashierBoardPage>(await cashier.GetAsync(incoming
            + $"?areaId={location.AreaId}&search=BOARD-EXTRA"));
        Assert.Empty(differentAreaSearch.Items);
        Assert.Equal(0, differentAreaSearch.TotalCount);

        Assert.Equal(HttpStatusCode.BadRequest, (await cashier.GetAsync(incoming + "?page=0")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await cashier.GetAsync(incoming + "?search=" + new string('a', 51))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await cashier.GetAsync(incoming + $"?cashShiftId={Guid.NewGuid()}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await waiter.GetAsync(paid)).StatusCode);
        using var invalidToken = new HttpRequestMessage(HttpMethod.Get, paid);
        invalidToken.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "invalid-token");
        Assert.Equal(HttpStatusCode.Unauthorized, (await cashier.SendAsync(invalidToken)).StatusCode);
    }

    private static async Task AssertPaidCashierBoard(HttpClient cashier, Guid restaurantId, Guid shiftId,
        Guid billId, decimal amount)
    {
        var path = $"/api/restaurants/{restaurantId}/payment-requests/paid?cashShiftId={shiftId}&pageSize=1";
        var page = await Read<CashierBoardPage>(await cashier.GetAsync(path));
        var card = Assert.Single(page.Items);
        Assert.Equal(billId, card.BillId);
        Assert.Equal("Paid", card.Status);
        Assert.Equal(amount, card.TotalAmount);
        Assert.Equal(amount, page.TotalAmount);
        Assert.Equal(0, page.UnbilledCount);
        Assert.NotNull(card.PaidAt);
        Assert.NotEmpty(card.Tables);
        Assert.Equal(shiftId, page.CashShiftId);
        var filtered = await Read<CashierBoardPage>(await cashier.GetAsync(path + "&search=" + card.BillCode));
        Assert.Equal(billId, Assert.Single(filtered.Items).BillId);
        Assert.Equal(1, filtered.TotalCount);
    }

    private sealed record CashierBoardPage(CashierBoardCard[] Items, int TotalCount, Guid CashShiftId,
        decimal? TotalAmount, decimal KnownTotalAmount, int UnbilledCount);

    private sealed record CashierBoardCard(Guid? CheckoutId, Guid TableSessionId, Guid? BillId, string Status,
        string? BillCode, decimal? TotalAmount, Guid? TableId, string? TableCode, Guid? AreaId, string? AreaName,
        int PartySize, DateTimeOffset? OpenedAt, DateTimeOffset? PaidAt, CashierBoardTable[] Tables);

    private sealed record CashierBoardTable(Guid TableId, string TableCode, Guid AreaId, string AreaName);
}
