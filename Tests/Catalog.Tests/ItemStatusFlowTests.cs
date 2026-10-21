using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Catalog.DAL.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Catalog.Tests;

public sealed class ItemStatusFlowTests
{
    [Fact]
    public async Task ManagerCanOpenReportOutOfStockAndDiscontinueWithoutChangingItemDetailsOrPrices()
    {
        using var application = await ItemTestApplication.Create();
        var categoryId = await application.AddCategory();
        var item = await application.CreateItem(categoryId);
        var id = item.GetProperty("itemId").GetGuid();
        await application.AddRecipe(id);
        await AddPrice(application, id);

        foreach (var availability in new[] { "Available", "OutOfStock", "Available", "Inactive" })
        {
            var previousVersion = item.GetProperty("version").GetString();
            var response = await SetStatus(application, item, availability);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            item = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("result");
            Assert.Equal(availability, item.GetProperty("availability").GetString());
            Assert.NotEqual(previousVersion, item.GetProperty("version").GetString());
            Assert.Equal("vegetarian-soup", item.GetProperty("itemCode").GetString());
            Assert.Equal(categoryId, item.GetProperty("categoryId").GetGuid());
            Assert.Equal("A seasonal vegetable soup.", item.GetProperty("description").GetString());
        }

        await using var db = application.OpenDatabase();
        Assert.Equal("Inactive", (await db.Items.SingleAsync()).Availability);
        Assert.Equal(100m, (await db.Prices.SingleAsync()).Amount);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("future")]
    [InlineData("expired")]
    [InlineData("channel-only")]
    [InlineData("daily-only")]
    [InlineData("duplicate")]
    [InlineData("negative")]
    public async Task OpeningRequiresExactlyOneCurrentlyValidStandardPrice(string scenario)
    {
        using var application = await ItemTestApplication.Create();
        var item = await application.CreateItem(await application.AddCategory());
        var id = item.GetProperty("itemId").GetGuid();
        await application.AddRecipe(id);
        if (scenario != "missing")
        {
            await AddPrice(application, id, scenario);
        }

        if (scenario == "duplicate")
        {
            await AddPrice(application, id);
        }

        Assert.Equal(HttpStatusCode.Conflict, (await SetStatus(application, item, "Available")).StatusCode);
        await using var db = application.OpenDatabase();
        var stored = await db.Items.SingleAsync();
        Assert.Equal("Inactive", stored.Availability);
        Assert.Equal(item.GetProperty("version").GetString(), Convert.ToBase64String(stored.Version));
    }

    [Fact]
    public async Task StandardPriceStartingNowWithZeroAmountAllowsOpening()
    {
        using var application = await ItemTestApplication.Create();
        var item = await application.CreateItem(await application.AddCategory());
        await application.AddRecipe(item.GetProperty("itemId").GetGuid());
        await AddPrice(application, item.GetProperty("itemId").GetGuid(), "starts-now-zero");
        Assert.Equal(HttpStatusCode.OK, (await SetStatus(application, item, "Available")).StatusCode);
    }

    [Fact]
    public async Task InactiveCategoryBlocksOpeningButDoesNotPreventDiscontinuing()
    {
        using var application = await ItemTestApplication.Create();
        var categoryId = await application.AddCategory();
        var item = await application.CreateItem(categoryId);
        await application.AddRecipe(item.GetProperty("itemId").GetGuid());
        await AddPrice(application, item.GetProperty("itemId").GetGuid());
        await using (var db = application.OpenDatabase())
        {
            (await db.Categories.SingleAsync()).IsActive = false;
            await db.SaveChangesAsync();
        }

        Assert.Equal(HttpStatusCode.Conflict, (await SetStatus(application, item, "Available")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SetStatus(application, item, "Inactive")).StatusCode);
    }

    [Fact]
    public async Task KitchenCanReadAndReportOutOfStockButCannotOpenDiscontinueOrEditItems()
    {
        using var application = await ItemTestApplication.Create();
        var categoryId = await application.AddCategory();
        var item = await application.CreateItem(categoryId);
        var id = item.GetProperty("itemId").GetGuid();
        await application.AddRecipe(id);
        await AddPrice(application, id);
        var opened = await SetStatus(application, item, "Available");
        Assert.Equal(HttpStatusCode.OK, opened.StatusCode);
        application.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "kitchen");

        Assert.Equal(HttpStatusCode.OK, (await application.Client.GetAsync("/api/items")).StatusCode);
        item = (await application.Client.GetFromJsonAsync<JsonElement>($"/api/items/{id}")).GetProperty("result");
        var response = await SetStatus(application, item, "OutOfStock");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        item = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("result");
        Assert.Equal("OutOfStock", item.GetProperty("availability").GetString());

        Assert.Equal(HttpStatusCode.Forbidden, (await SetStatus(application, item, "Available")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await SetStatus(application, item, "Inactive")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await application.Client.PutAsJsonAsync($"/api/items/{id}", new
        {
            categoryId,
            itemName = "Kitchen cannot rename",
            version = item.GetProperty("version").GetString()
        })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await application.Client.PostAsJsonAsync("/api/items", new
        {
            categoryId,
            itemName = "Kitchen cannot create"
        })).StatusCode);
        await using var db = application.OpenDatabase();
        Assert.Equal("OutOfStock", (await db.Items.SingleAsync()).Availability);
    }

    [Fact]
    public async Task KitchenCannotBringADiscontinuedItemBackByMarkingItOutOfStock()
    {
        using var application = await ItemTestApplication.Create();
        var item = await application.CreateItem(await application.AddCategory());
        application.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "kitchen");
        Assert.Equal(HttpStatusCode.Conflict, (await SetStatus(application, item, "OutOfStock")).StatusCode);
    }

    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData("revoked", HttpStatusCode.Unauthorized)]
    [InlineData("disabled", HttpStatusCode.Unauthorized)]
    [InlineData("waiter", HttpStatusCode.Forbidden)]
    public async Task UnauthorizedActorsCannotChangeStatus(string? token, HttpStatusCode expected)
    {
        using var application = await ItemTestApplication.Create();
        var item = await application.CreateItem(await application.AddCategory());
        application.Client.DefaultRequestHeaders.Authorization = token is null
            ? null
            : new AuthenticationHeaderValue("Bearer", token);
        Assert.Equal(expected, (await SetStatus(application, item, "Inactive")).StatusCode);
        Assert.Equal(expected, (await SetStatus(application, item, "OutOfStock")).StatusCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("available")]
    [InlineData(" Available ")]
    [InlineData("unknown")]
    public async Task InvalidStatusesAreRejected(string? availability)
    {
        using var application = await ItemTestApplication.Create();
        var item = await application.CreateItem(await application.AddCategory());
        Assert.Equal(HttpStatusCode.BadRequest, (await SetStatus(application, item, availability)).StatusCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("invalid")]
    [InlineData("AQ==")]
    public async Task MissingOrMalformedVersionsAreRejected(string? version)
    {
        using var application = await ItemTestApplication.Create();
        var item = await application.CreateItem(await application.AddCategory());
        var response = await application.Client.PatchAsJsonAsync($"/api/items/{item.GetProperty("itemId").GetGuid()}/status", new
        {
            availability = "Inactive",
            version
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task StaleStatusAndEditRequestsCannotOverwriteEachOtherAndSameStatusIsIdempotent()
    {
        using var application = await ItemTestApplication.Create();
        var categoryId = await application.AddCategory();
        var original = await application.CreateItem(categoryId);
        var id = original.GetProperty("itemId").GetGuid();
        await application.AddRecipe(id);
        await AddPrice(application, id);
        var response = await SetStatus(application, original, "Available");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var opened = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("result");
        Assert.Equal(HttpStatusCode.Conflict, (await SetStatus(application, original, "Inactive")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await application.Client.PutAsJsonAsync($"/api/items/{id}", new
        {
            categoryId,
            itemName = "Stale edit",
            version = original.GetProperty("version").GetString()
        })).StatusCode);

        var repeated = await SetStatus(application, opened, "Available");
        Assert.Equal(HttpStatusCode.OK, repeated.StatusCode);
        Assert.Equal(opened.GetProperty("version").GetString(),
            (await repeated.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("result").GetProperty("version").GetString());

        var edited = await application.Client.PutAsJsonAsync($"/api/items/{id}", new
        {
            categoryId,
            itemName = "Fresh edit",
            version = opened.GetProperty("version").GetString()
        });
        Assert.Equal(HttpStatusCode.OK, edited.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await SetStatus(application, opened, "Inactive")).StatusCode);
    }

    [Fact]
    public async Task MissingAndInvalidItemIdsAreRejected()
    {
        using var application = await ItemTestApplication.Create();
        var body = new
        {
            availability = "Inactive",
            version = Convert.ToBase64String(new byte[8])
        };
        Assert.Equal(HttpStatusCode.NotFound,
            (await application.Client.PatchAsJsonAsync($"/api/items/{Guid.NewGuid()}/status", body)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await application.Client.PatchAsJsonAsync($"/api/items/{Guid.Empty}/status", body)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await application.Client.PatchAsJsonAsync("/api/items/not-a-guid/status", body)).StatusCode);
    }

    private static Task<HttpResponseMessage> SetStatus(
        ItemTestApplication application,
        JsonElement item,
        string? availability)
    {
        return application.Client.PatchAsJsonAsync($"/api/items/{item.GetProperty("itemId").GetGuid()}/status", new
        {
            availability,
            version = item.GetProperty("version").GetString()
        });
    }

    private static async Task AddPrice(ItemTestApplication application, Guid itemId, string scenario = "valid")
    {
        await using var db = application.OpenDatabase();
        db.Prices.Add(new Price
        {
            PriceId = Guid.NewGuid(),
            ItemId = itemId,
            Channel = scenario == "channel-only" ? "DineIn" : "AllChannels",
            Amount = scenario == "negative" ? -1m : scenario == "starts-now-zero" ? 0m : 100m,
            EffectiveFrom = scenario == "future"
                ? ItemTestApplication.Now.AddDays(1).UtcDateTime
                : scenario == "starts-now-zero"
                    ? ItemTestApplication.Now.UtcDateTime
                    : ItemTestApplication.Now.AddDays(-1).UtcDateTime,
            EffectiveTo = scenario == "expired" ? ItemTestApplication.Now.UtcDateTime : null,
            DailyStart = scenario == "daily-only" ? new TimeOnly(0, 0) : null,
            DailyEnd = scenario == "daily-only" ? new TimeOnly(23, 59) : null
        });
        await db.SaveChangesAsync();
    }
}
