using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Catalog.DAL.Models;
using Xunit;

namespace Catalog.Tests;

public sealed class MenuFlowTests
{
    [Theory]
    [InlineData("waiter", HttpStatusCode.OK)]
    [InlineData("manager", HttpStatusCode.OK)]
    [InlineData("kitchen", HttpStatusCode.Forbidden)]
    [InlineData("customer", HttpStatusCode.Forbidden)]
    [InlineData("disabled", HttpStatusCode.Unauthorized)]
    [InlineData("revoked", HttpStatusCode.Unauthorized)]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    public async Task Menu_EnforcesStaffAccess(string? token, HttpStatusCode expected)
    {
        using var app = await ItemTestApplication.Create();
        app.Client.DefaultRequestHeaders.Authorization = token is null ? null : new AuthenticationHeaderValue("Bearer", token);
        var response = await app.Client.GetAsync("/api/menu");
        Assert.Equal(expected, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
    }

    [Fact]
    public async Task Guest_CookieIsVerifiedForRequestedSession_OnListAndDetail()
    {
        using var app = await ItemTestApplication.Create();
        var id = await SeedItem(app);
        app.Client.DefaultRequestHeaders.Authorization = null;
        var sessionId = MenuGuestClient.SessionId;
        var query = $"?tableSessionId={sessionId}";
        Assert.Equal(HttpStatusCode.Unauthorized, (await app.Client.GetAsync("/api/menu" + query)).StatusCode);
        app.Client.DefaultRequestHeaders.Add("Cookie", $"guest_access_{sessionId:N}={MenuGuestClient.Token}");
        Assert.Equal(HttpStatusCode.OK, (await app.Client.GetAsync("/api/menu" + query)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await app.Client.GetAsync($"/api/menu/{id}" + query)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await app.Client.GetAsync($"/api/menu?tableSessionId={Guid.NewGuid()}")).StatusCode);
        app.Client.DefaultRequestHeaders.Remove("Cookie");
        app.Client.DefaultRequestHeaders.Add("Cookie", $"guest_access_{sessionId:N}={new string('B', 64)}");
        Assert.Equal(HttpStatusCode.Unauthorized, (await app.Client.GetAsync("/api/menu" + query)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await app.Client.GetAsync($"/api/menu/{id}" + query)).StatusCode);
    }

    [Fact]
    public async Task Guest_VerificationUnavailable_DoesNotFallBackToStaff()
    {
        using var app = await ItemTestApplication.Create();
        var sessionId = MenuGuestClient.SessionId;
        app.Client.DefaultRequestHeaders.Add("Cookie", $"guest_access_{sessionId:N}={new string('C', 64)}");
        Assert.Equal(HttpStatusCode.ServiceUnavailable,
            (await app.Client.GetAsync($"/api/menu?tableSessionId={sessionId}")).StatusCode);
    }

    [Fact]
    public async Task Menu_RequiredGroupWithoutSelectableOptions_CannotBeOrdered()
    {
        using var app = await ItemTestApplication.Create();
        var id = await SeedItem(app);
        await using (var db = app.OpenDatabase())
        {
            db.ItemOptionGroups.Add(new ItemOptionGroup
            {
                ItemId = id,
                MinSelection = 1,
                MaxSelection = 1,
                OptionGroup = new OptionGroup
                {
                    OptionGroupId = Guid.NewGuid(),
                    OptionGroupName = "Size",
                    Options = new List<Option>
                    {
                        Option("Large", "OutOfStock")
                    }
                }
            });
            await db.SaveChangesAsync();
        }

        var result = await Result(app, $"/api/menu/{id}");
        Assert.False(result.GetProperty("canOrder").GetBoolean());
        Assert.Equal("RequiredOptionsUnavailable", result.GetProperty("unavailableReason").GetString());
    }

    [Fact]
    public async Task Menu_HidesInactiveItemsAndCategories_ShowsOutOfStock()
    {
        using var app = await ItemTestApplication.Create();
        var available = await SeedItem(app, "Available");
        var soldOut = await SeedItem(app, "OutOfStock");
        var inactive = await SeedItem(app, "Inactive");
        var hiddenCategory = await SeedItem(app, categoryActive: false);
        app.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "waiter");
        var result = await Result(app, "/api/menu");
        Assert.Equal(2, result.GetProperty("totalCount").GetInt32());
        Assert.True((await Result(app, $"/api/menu/{available}")).GetProperty("canOrder").GetBoolean());
        var detail = await Result(app, $"/api/menu/{soldOut}");
        Assert.False(detail.GetProperty("canOrder").GetBoolean());
        Assert.Equal("OutOfStock", detail.GetProperty("unavailableReason").GetString());
        Assert.Equal(50000m, detail.GetProperty("price").GetDecimal());
        Assert.Equal(HttpStatusCode.NotFound, (await app.Client.GetAsync($"/api/menu/{inactive}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await app.Client.GetAsync($"/api/menu/{hiddenCategory}")).StatusCode);
    }

    [Fact]
    public async Task Menu_PriceUsesChannelAndLocalWindow_ExpiredPricesIgnored()
    {
        using var app = await ItemTestApplication.Create();
        var id = await SeedItem(app);
        await using (var db = app.OpenDatabase())
        {
            var channel = Price(id, 45000m, "DineIn");
            var timed = Price(id, 40000m);
            timed.DailyStart = new TimeOnly(15, 0);
            timed.DailyEnd = new TimeOnly(16, 0);
            var timedChannel = Price(id, 35000m, "DineIn");
            timedChannel.DailyStart = timed.DailyStart;
            timedChannel.DailyEnd = timed.DailyEnd;
            var expired = Price(id, 1m, "DineIn");
            expired.EffectiveTo = ItemTestApplication.Now.UtcDateTime;
            var future = Price(id, 2m, "DineIn");
            future.EffectiveFrom = ItemTestApplication.Now.AddDays(1).UtcDateTime;
            db.Prices.AddRange(channel, timed, timedChannel, expired, future);
            await db.SaveChangesAsync();
        }

        var result = await Result(app, $"/api/menu/{id}");
        Assert.Equal(35000m, result.GetProperty("price").GetDecimal());
        Assert.True(result.GetProperty("canOrder").GetBoolean());
        Assert.Equal(ItemTestApplication.Now, result.GetProperty("evaluatedAt").GetDateTimeOffset());
        var list = await Result(app, "/api/menu");
        Assert.Equal(35000m, list.GetProperty("items")[0].GetProperty("price").GetDecimal());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Menu_MissingOrConflictingPrice_CannotBeOrdered(bool conflict)
    {
        using var app = await ItemTestApplication.Create();
        var id = await SeedItem(app, withPrice: conflict);
        if (conflict)
        {
            await using var db = app.OpenDatabase();
            db.Prices.Add(Price(id, 55000m));
            await db.SaveChangesAsync();
        }

        var result = await Result(app, $"/api/menu/{id}");
        Assert.False(result.GetProperty("canOrder").GetBoolean());
        Assert.Equal(JsonValueKind.Null, result.GetProperty("price").ValueKind);
        Assert.Equal("PriceUnavailable", result.GetProperty("unavailableReason").GetString());
    }

    [Fact]
    public async Task Menu_ReturnsOnlyLinkedVisibleOptions_WithSelectionLimits()
    {
        using var app = await ItemTestApplication.Create();
        var id = await SeedItem(app);
        await using (var db = app.OpenDatabase())
        {
            var group = new OptionGroup
            {
                OptionGroupId = Guid.NewGuid(),
                OptionGroupName = "Toppings",
                Options = new List<Option>
                {
                    Option("A", "Available"),
                    Option("B", "OutOfStock"),
                    Option("C", "Inactive")
                }
            };
            db.ItemOptionGroups.Add(new ItemOptionGroup
            {
                ItemId = id,
                OptionGroup = group,
                MinSelection = 1,
                MaxSelection = 2
            });
            db.OptionGroups.Add(new OptionGroup
            {
                OptionGroupId = Guid.NewGuid(),
                OptionGroupName = "Unrelated",
                Options = new List<Option>
                {
                    Option("Unrelated", "Available")
                }
            });
            await db.SaveChangesAsync();
        }

        var result = await Result(app, $"/api/menu/{id}");
        var groups = result.GetProperty("optionGroups");
        Assert.Equal(1, groups.GetArrayLength());
        Assert.Equal(1, groups[0].GetProperty("minSelection").GetInt32());
        Assert.Equal(2, groups[0].GetProperty("maxSelection").GetInt32());
        var options = groups[0].GetProperty("options");
        Assert.Equal(2, options.GetArrayLength());
        Assert.True(options[0].GetProperty("canSelect").GetBoolean());
        Assert.Equal(5000m, options[0].GetProperty("extraPrice").GetDecimal());
        Assert.False(options[1].GetProperty("canSelect").GetBoolean());
        Assert.False(result.TryGetProperty("recipeVersionId", out _));
    }

    [Fact]
    public async Task Menu_FiltersAndPaginates()
    {
        using var app = await ItemTestApplication.Create();
        var categoryId = await app.AddCategory();
        await SeedItem(app, name: "Beef Soup", categoryId: categoryId);
        await SeedItem(app, name: "Beef Rice", categoryId: categoryId);
        await SeedItem(app, name: "Beef Noodles");
        var result = await Result(app, $"/api/menu?categoryId={categoryId}&search=bEeF&page=2&pageSize=1");
        Assert.Equal(2, result.GetProperty("totalCount").GetInt32());
        Assert.Equal("Beef Soup", result.GetProperty("items")[0].GetProperty("itemName").GetString());
    }

    [Theory]
    [InlineData("page=0")]
    [InlineData("pageSize=101")]
    [InlineData("page=2147483647&pageSize=100")]
    [InlineData("channel=Unknown")]
    [InlineData("categoryId=bad-id")]
    [InlineData("tableSessionId=00000000-0000-0000-0000-000000000000")]
    public async Task Menu_InvalidQuery_Returns400(string query)
    {
        using var app = await ItemTestApplication.Create();
        Assert.Equal(HttpStatusCode.BadRequest, (await app.Client.GetAsync("/api/menu?" + query)).StatusCode);
    }

    private static async Task<JsonElement> Result(ItemTestApplication app, string url)
    {
        var response = await app.Client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("result");
    }

    private static async Task<Guid> SeedItem(
        ItemTestApplication app, string availability = "Available", bool categoryActive = true,
        bool withPrice = true, string name = "Noodle Soup", Guid? categoryId = null)
    {
        var category = categoryId ?? await app.AddCategory(Guid.NewGuid().ToString(), categoryActive);
        await using var db = app.OpenDatabase();
        var item = new Item
        {
            ItemId = Guid.NewGuid(),
            CategoryId = category,
            ItemCode = Guid.NewGuid().ToString("N")[..12],
            ItemName = name,
            Availability = availability
        };
        db.Items.Add(item);
        if (withPrice)
        {
            db.Prices.Add(Price(item.ItemId, 50000m));
        }

        await db.SaveChangesAsync();
        await app.AddRecipe(item.ItemId);
        return item.ItemId;
    }

    private static Price Price(Guid id, decimal amount, string channel = "AllChannels")
    {
        return new Price
        {
            PriceId = Guid.NewGuid(),
            ItemId = id,
            Channel = channel,
            Amount = amount,
            EffectiveFrom = ItemTestApplication.Now.AddDays(-1).UtcDateTime
        };
    }

    private static Option Option(string name, string availability)
    {
        return new Option
        {
            OptionId = Guid.NewGuid(),
            OptionName = name,
            Availability = availability,
            ExtraPrice = 5000m
        };
    }
}
