using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using ApiGateway.DTOs.Restaurant;
using Xunit;

namespace Restaurant.Tests;

public sealed partial class GatewayFlowTests
{
    [Theory]
    [InlineData("Asia/Ho_Chi_Minh", "2030-01-06T18:00:00Z", "2030-01-06T17:00:00-10:00", true)]
    [InlineData("Asia/Ho_Chi_Minh", "2030-01-07T16:00:00Z", "2030-01-07T17:30:00Z", false)]
    [InlineData("America/Los_Angeles", "2030-01-07T06:00:00Z", "2030-01-07T08:30:00Z", false)]
    [InlineData("America/Los_Angeles", "2030-01-07T09:00:00Z", "2030-01-07T18:00:00Z", true)]
    [InlineData("Pacific/Honolulu", "2030-01-07T01:00:00Z", "2030-01-07T03:00:00Z", true)]
    [InlineData("Asia/Ho_Chi_Minh", "2030-01-07T01:00:00Z", "2030-01-08T03:00:00Z", false)]
    public async Task AnonymousBookingUsesTodaysDateInTheRestaurantTimeZone(
        string timeZone, string now, string start, bool accepted)
    {
        _reservationClock.Now = DateTimeOffset.Parse(now, CultureInfo.InvariantCulture);
        var body = RestaurantBody();
        body.TimeZone = timeZone;
        body.OpeningHours = Enumerable.Range(0, 7).Select(day => new OpeningHourDto
        {
            DayOfWeek = day,
            OpenAt = "00:00",
            CloseAt = "23:59"
        }).ToList();
        var restaurant = await Read<RestaurantDto>(await _http.PostAsJsonAsync("/api/restaurants", body), HttpStatusCode.Created);
        _http.DefaultRequestHeaders.Authorization = null;

        using var response = await _http.PostAsJsonAsync("/api/reservations", new
        {
            RestaurantId = restaurant.RestaurantId,
            ContactName = "Khách đặt trong ngày",
            Phone = "0900000000",
            PartySize = 2,
            StartAt = start
        });

        Assert.Equal(accepted ? HttpStatusCode.Created : HttpStatusCode.BadRequest, response.StatusCode);
        if (!accepted)
        {
            Assert.Contains("trong ngày", await response.Content.ReadAsStringAsync());
        }
    }
}
