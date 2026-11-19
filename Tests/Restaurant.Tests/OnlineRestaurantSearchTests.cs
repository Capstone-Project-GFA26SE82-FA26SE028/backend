using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Restaurant.DAL.Models;

namespace Restaurant.Tests;

public sealed partial class GatewayFlowTests
{
    [Fact]
    public async Task AnonymousRestaurantSearchMatchesNamesAndAddressesBeforePaging()
    {
        using (var scope = _restaurant.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RestaurantDbContext>();
            foreach (var pair in new[] { ("seoul BBQ", "district 1"), ("Korean food", "seoul street"), ("Other", "Hanoi") })
            {
                db.Restaurants.Add(new Restaurant.DAL.Models.Restaurant
                {
                    RestaurantId = Guid.NewGuid(),
                    RestaurantName = pair.Item1,
                    Address = pair.Item2,
                    TimeZone = "UTC"
                });
            }

            await db.SaveChangesAsync();
        }

        _http.DefaultRequestHeaders.Authorization = null;
        var page = await Read<OnlineSearchPage>(await _http.GetAsync("/api/online/restaurants?search=seoul&page=1&pageSize=1"));
        Assert.Equal(2, page.TotalCount);
        var first = Assert.Single(page.Items);
        var next = await Read<OnlineSearchPage>(await _http.GetAsync("/api/online/restaurants?search=seoul&page=2&pageSize=1"));
        Assert.Equal(2, next.TotalCount);
        Assert.NotEqual(first.RestaurantId, Assert.Single(next.Items).RestaurantId);
        Assert.Equal(HttpStatusCode.BadRequest, (await _http.GetAsync("/api/online/restaurants?pageSize=101")).StatusCode);
    }
}

internal sealed record OnlineSearchItem(Guid RestaurantId, string RestaurantName, string Address);
internal sealed record OnlineSearchPage(int TotalCount, OnlineSearchItem[] Items);
