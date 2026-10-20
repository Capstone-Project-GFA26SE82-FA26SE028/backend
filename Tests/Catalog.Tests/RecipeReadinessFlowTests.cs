using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Catalog.DAL.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Catalog.Tests;

public sealed class RecipeReadinessFlowTests
{
    [Theory]
    [InlineData("missing")]
    [InlineData("future")]
    [InlineData("expired")]
    [InlineData("duplicate")]
    [InlineData("valid")]
    public async Task OpeningAndMenu_RequireExactlyOneActiveRecipe(string scenario)
    {
        using var app = await ItemTestApplication.Create();
        var item = await app.CreateItem(await app.AddCategory());
        var id = item.GetProperty("itemId").GetGuid();
        await using (var db = app.OpenDatabase())
        {
            db.Prices.Add(new Price
            {
                PriceId = Guid.NewGuid(),
                ItemId = id,
                Channel = "AllChannels",
                Amount = 50000m,
                EffectiveFrom = ItemTestApplication.Now.AddDays(-1).UtcDateTime
            });
            await db.SaveChangesAsync();
        }

        if (scenario != "missing")
        {
            await app.AddRecipe(id,
                scenario == "future" ? ItemTestApplication.Now.AddDays(1).UtcDateTime
                    : scenario == "valid" ? ItemTestApplication.Now.UtcDateTime : ItemTestApplication.Now.AddDays(-1).UtcDateTime,
                scenario == "expired" ? ItemTestApplication.Now.UtcDateTime : null);
        }

        if (scenario == "duplicate")
        {
            await app.AddRecipe(id);
        }

        var response = await app.Client.PatchAsJsonAsync($"/api/items/{id}/status", new
        {
            availability = "Available",
            version = item.GetProperty("version").GetString()
        });
        Assert.Equal(scenario == "valid" ? HttpStatusCode.OK : HttpStatusCode.Conflict, response.StatusCode);
        await using (var db = app.OpenDatabase())
        {
            var stored = await db.Items.SingleAsync();
            if (scenario != "valid")
            {
                Assert.Equal("Inactive", stored.Availability);
                Assert.Equal(item.GetProperty("version").GetString(), Convert.ToBase64String(stored.Version));
                // Simulate legacy Available data. Reading menu must still disable it.
                stored.Availability = "Available";
                await db.SaveChangesAsync();
            }
        }

        var menu = await app.Client.GetFromJsonAsync<JsonElement>($"/api/menu/{id}");
        Assert.Equal(scenario == "valid", menu.GetProperty("result").GetProperty("canOrder").GetBoolean());
        if (scenario != "valid")
        {
            Assert.Equal("RecipeUnavailable", menu.GetProperty("result").GetProperty("unavailableReason").GetString());
        }
    }

    [Theory]
    [InlineData(false, "same")]
    [InlineData(false, "earlier")]
    [InlineData(false, "sql-precision")]
    [InlineData(true, "same")]
    [InlineData(true, "earlier")]
    [InlineData(true, "sql-precision")]
    public async Task RecipeTimeline_RejectsInsertingBeforeOrAtLatest_WithoutChangingHistory(bool forOption, string scenario)
    {
        using var app = await ItemTestApplication.Create();
        var itemId = await OptionTestData.Item(app, "Soup");
        var ownerId = itemId;
        if (forOption)
        {
            var group = await OptionTestData.Group(app, "Extra");
            ownerId = await OptionTestData.Option(app, group, "Beef", 15000m);
        }

        var path = forOption ? $"/api/options/{ownerId}/recipes" : $"/api/items/{ownerId}/recipes";
        var firstStart = new DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var latestStart = firstStart.AddDays(10);
        Assert.Equal(HttpStatusCode.Created, (await app.Client.PostAsJsonAsync(path, Recipe(firstStart))).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await app.Client.PostAsJsonAsync(path, Recipe(latestStart))).StatusCode);
        var attempted = scenario switch
        {
            "earlier" => latestStart.AddDays(-1),
            "sql-precision" => latestStart.AddMilliseconds(1),
            _ => latestStart
        };
        Assert.Equal(HttpStatusCode.Conflict, (await app.Client.PostAsJsonAsync(path, Recipe(attempted))).StatusCode);
        var history = (await app.Client.GetFromJsonAsync<JsonElement>(path)).GetProperty("result");
        Assert.Equal(2, history.GetArrayLength());
        Assert.Equal(2, history[0].GetProperty("version").GetInt32());
        Assert.Equal(JsonValueKind.Null, history[0].GetProperty("effectiveTo").ValueKind);
        Assert.Equal(latestStart, history[1].GetProperty("effectiveTo").GetDateTime());
        var previous = (await app.Client.GetFromJsonAsync<JsonElement>(
            $"{path}/active?at={Uri.EscapeDataString(latestStart.AddSeconds(-1).ToString("O"))}")).GetProperty("result");
        var next = (await app.Client.GetFromJsonAsync<JsonElement>(
            $"{path}/active?at={Uri.EscapeDataString(latestStart.ToString("O"))}")).GetProperty("result");
        Assert.Equal(1, previous.GetProperty("version").GetInt32());
        Assert.Equal(2, next.GetProperty("version").GetInt32());
    }

    [Fact]
    public async Task RecipeOutsideSqlDateRange_IsRejected()
    {
        using var app = await ItemTestApplication.Create();
        var id = await OptionTestData.Item(app, "Soup");
        var response = await app.Client.PostAsJsonAsync($"/api/items/{id}/recipes",
            Recipe(new DateTime(1000, 1, 1, 0, 0, 0, DateTimeKind.Utc)));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await using var db = app.OpenDatabase();
        Assert.Empty(await db.RecipeVersions.ToListAsync());
    }

    private static object Recipe(DateTime from)
    {
        return new
        {
            effectiveFrom = from,
            ingredients = new[]
            {
                new
                {
                    ingredientRef = Guid.NewGuid(),
                    unitRef = Guid.NewGuid(),
                    quantity = 100m,
                    conversionFactor = 0.001m
                }
            }
        };
    }
}
