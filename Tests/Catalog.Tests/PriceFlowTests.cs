using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Catalog.DAL.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Catalog.Tests;

public sealed class PriceFlowTests
{
    [Fact]
    public async Task ListSupportsItemChannelAndPaginationAndValidatesMissingResources()
    {
        using var app = await PriceTestApplication.Create();
        var first = await app.AddItem();
        var second = await app.AddItem();
        await app.CreatePrice(first);
        await app.CreatePrice(first, 90m, "DineIn");
        await app.CreatePrice(second);
        var list = await app.Client.GetFromJsonAsync<JsonElement>(
            $"/api/prices?itemId={first}&channel=DineIn&page=1&pageSize=1");
        var result = list.GetProperty("result");
        Assert.Equal(1, result.GetProperty("totalCount").GetInt32());
        Assert.Equal(first, result.GetProperty("items")[0].GetProperty("itemId").GetGuid());
        Assert.Equal(HttpStatusCode.BadRequest, (await app.Client.GetAsync("/api/prices?page=0")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await app.Client.GetAsync("/api/prices?pageSize=101")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await app.Client.GetAsync($"/api/prices?itemId={Guid.NewGuid()}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await app.Client.GetAsync($"/api/prices/{Guid.NewGuid()}")).StatusCode);
    }

    [Fact]
    public async Task FutureUpdatesCannotMoveIntoThePastOrOverlapAnotherSchedule()
    {
        using var app = await PriceTestApplication.Create();
        var itemId = await app.AddItem();
        var now = app.Clock.GetUtcNow();
        var first = await app.CreatePrice(itemId, from: now.AddDays(1), to: now.AddDays(2));
        await app.CreatePrice(itemId, 200m, from: now.AddDays(2));
        var id = first.GetProperty("priceId").GetGuid();
        Assert.Equal(HttpStatusCode.BadRequest,
            (await app.Mutate(HttpMethod.Put, $"/api/prices/{id}", new
            {
                channel = "AllChannels",
                amount = 150m,
                effectiveFrom = now.AddDays(-1).ToString("O"),
                effectiveTo = now.AddDays(2).ToString("O")
            })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,
            (await app.Mutate(HttpMethod.Put, $"/api/prices/{id}", new
            {
                channel = "AllChannels",
                amount = 150m,
                effectiveFrom = now.AddDays(1).ToString("O")
            })).StatusCode);
    }

    [Fact]
    public async Task ExpiredPricesAreReadOnlyAndCannotBeUsedAtTheirEndBoundary()
    {
        using var app = await PriceTestApplication.Create();
        var itemId = await app.AddItem();
        var now = app.Clock.GetUtcNow();
        var old = await app.CreatePrice(itemId, from: now.AddDays(-1), to: now);
        var id = old.GetProperty("priceId").GetGuid();
        Assert.Equal(HttpStatusCode.NotFound,
            (await app.Client.GetAsync($"/api/items/{itemId}/price?channel=DineIn")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,
            (await app.Mutate(HttpMethod.Delete, $"/api/prices/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,
            (await app.Mutate(HttpMethod.Post, $"/api/prices/{id}/replace", new { amount = 50m })).StatusCode);
    }

    [Fact]
    public async Task AmbiguousStoredPricesDoNotProduceAnArbitraryWinner()
    {
        using var app = await PriceTestApplication.Create();
        var itemId = await app.AddItem();
        await app.CreatePrice(itemId);
        await using (var db = app.OpenDatabase())
        {
            db.Prices.Add(new Price
            {
                PriceId = Guid.NewGuid(),
                ItemId = itemId,
                Channel = "AllChannels",
                Amount = 200m,
                EffectiveFrom = app.Clock.GetUtcNow().AddDays(-1).UtcDateTime
            });
            await db.SaveChangesAsync();
        }

        Assert.Equal(HttpStatusCode.Conflict,
            (await app.Client.GetAsync($"/api/items/{itemId}/price?channel=DineIn")).StatusCode);
        await app.CreatePrice(itemId, 90m, "DineIn");
        Assert.False((await app.Applied(itemId)).GetProperty("hasValidStandardPrice").GetBoolean());
    }

    [Fact]
    public async Task ReplacementsRejectExpiredEndsAndClientTransitionTimes()
    {
        using var app = await PriceTestApplication.Create();
        var itemId = await app.AddItem();
        var current = await app.CreatePrice(itemId);
        var id = current.GetProperty("priceId").GetGuid();
        Assert.Equal(HttpStatusCode.BadRequest,
            (await app.Mutate(HttpMethod.Post, $"/api/prices/{id}/replace", new
            {
                amount = 50m,
                effectiveTo = app.Clock.GetUtcNow().ToString("O")
            })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await app.Mutate(HttpMethod.Post, $"/api/prices/{id}/replace", new
            {
                amount = 50m,
                effectiveFrom = app.Clock.GetUtcNow().ToString("O")
            })).StatusCode);
        var detail = await app.Client.GetFromJsonAsync<JsonElement>($"/api/prices/{id}");
        Assert.Equal(current.GetProperty("amount").GetDecimal(),
            detail.GetProperty("result").GetProperty("amount").GetDecimal());
    }

    [Fact]
    public async Task FuturePriceCanBeCreatedReadUpdatedAndDeletedWithoutConditionalHeaders()
    {
        using var app = await PriceTestApplication.Create();
        var itemId = await app.AddItem();
        var start = app.Clock.GetUtcNow().AddDays(1);
        var created = await app.CreatePrice(itemId, 12.34m, from: start);
        var id = created.GetProperty("priceId").GetGuid();
        var detail = await app.Client.GetFromJsonAsync<JsonElement>($"/api/prices/{id}");
        Assert.Equal(created.GetProperty("amount").GetDecimal(),
            detail.GetProperty("result").GetProperty("amount").GetDecimal());
        var update = await app.Mutate(HttpMethod.Put, $"/api/prices/{id}", new
        {
            channel = "AllChannels",
            amount = 20.50m,
            effectiveFrom = start.ToString("O")
        });
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        var updated = (await update.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("result");
        Assert.Equal(20.50m, updated.GetProperty("amount").GetDecimal());
        Assert.Equal(HttpStatusCode.OK,
            (await app.Mutate(HttpMethod.Delete, $"/api/prices/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await app.Client.GetAsync($"/api/prices/{id}")).StatusCode);
        await using var db = app.OpenDatabase();
        Assert.Equal("Inactive", (await db.Items.SingleAsync()).Availability);
        Assert.Empty(await db.Prices.ToListAsync());
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1.234)]
    [InlineData(10000000000000000)]
    public async Task InvalidAmountsAreRejected(double amount)
    {
        using var app = await PriceTestApplication.Create();
        var itemId = await app.AddItem();
        var response = await app.Client.PostAsJsonAsync("/api/prices", new
        {
            itemId,
            channel = "AllChannels",
            amount,
            effectiveFrom = app.Clock.GetUtcNow().ToString("O")
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ZeroAndMaximumSchemaAmountsAreValid()
    {
        using var app = await PriceTestApplication.Create();
        var first = await app.AddItem();
        var second = await app.AddItem();
        Assert.Equal(0m, (await app.CreatePrice(first, 0m)).GetProperty("amount").GetDecimal());
        Assert.Equal(9999999999999999.99m,
            (await app.CreatePrice(second, 9999999999999999.99m)).GetProperty("amount").GetDecimal());
    }

    [Theory]
    [InlineData("2030-01-01T00:00:00", null, null, null)]
    [InlineData("1700-01-01T00:00:00Z", null, null, null)]
    [InlineData("2030-01-01T00:00:00Z", "2030-01-01T00:00:00Z", null, null)]
    [InlineData("2030-01-01T00:00:00Z", null, "14:00", null)]
    [InlineData("2030-01-01T00:00:00Z", null, "22:00", "02:00")]
    [InlineData("2030-01-01T00:00:00Z", null, "14:00", "14:00")]
    [InlineData("2030-01-01T00:00:00Z", null, "14:00:00.1", "15:00")]
    public async Task InvalidTimestampAndDailyWindowsAreRejected(string from, string? to, string? start, string? end)
    {
        using var app = await PriceTestApplication.Create();
        var itemId = await app.AddItem();
        var response = await app.Client.PostAsJsonAsync("/api/prices", new
        {
            itemId,
            channel = "AllChannels",
            amount = 10m,
            effectiveFrom = from,
            effectiveTo = to,
            dailyStart = start,
            dailyEnd = end
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task OffsetInputIsStoredAsUtcAndUnsupportedChannelsAreRejected()
    {
        using var app = await PriceTestApplication.Create();
        var itemId = await app.AddItem();
        var response = await app.Client.PostAsJsonAsync("/api/prices", new
        {
            itemId,
            channel = "AllChannels",
            amount = 25m,
            effectiveFrom = "2030-01-15T15:00:00+07:00"
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var result = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("result");
        Assert.Equal(app.Clock.GetUtcNow(), result.GetProperty("effectiveFrom").GetDateTimeOffset());
        Assert.Equal(HttpStatusCode.BadRequest,
            (await app.Client.PostAsJsonAsync("/api/prices", new
            {
                itemId,
                channel = "Takeaway",
                amount = 25m,
                effectiveFrom = "2030-01-16T00:00:00Z"
            })).StatusCode);
    }

    [Fact]
    public async Task SameScopeOverlapIsRejectedButAdjacentIntervalsAreAllowed()
    {
        using var app = await PriceTestApplication.Create();
        var itemId = await app.AddItem();
        var now = app.Clock.GetUtcNow();
        await app.CreatePrice(itemId, from: now.AddDays(-1), to: now);
        await app.CreatePrice(itemId, 200m, from: now, to: now.AddDays(1));
        var conflict = await app.Client.PostAsJsonAsync("/api/prices", new
        {
            itemId,
            channel = "AllChannels",
            amount = 300m,
            effectiveFrom = now.AddHours(-1).ToString("O"),
            effectiveTo = now.AddHours(1).ToString("O")
        });
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        Assert.Equal(200m, (await app.Applied(itemId)).GetProperty("amount").GetDecimal());
    }

    [Fact]
    public async Task PriorityAndBusinessTimeWindowBoundariesAreApplied()
    {
        using var app = await PriceTestApplication.Create();
        var itemId = await app.AddItem();
        await app.CreatePrice(itemId, 100m);
        Assert.Equal(100m, (await app.Applied(itemId)).GetProperty("amount").GetDecimal());
        await app.CreatePrice(itemId, 90m, "DineIn");
        Assert.Equal(90m, (await app.Applied(itemId)).GetProperty("amount").GetDecimal());
        await app.CreatePrice(itemId, 80m, dailyStart: "14:00", dailyEnd: "17:00");
        Assert.Equal(80m, (await app.Applied(itemId)).GetProperty("amount").GetDecimal());
        await app.CreatePrice(itemId, 75m, "DineIn", dailyStart: "14:00", dailyEnd: "17:00");
        var now = app.Clock.GetUtcNow();
        Assert.Equal(75m, (await app.Applied(itemId)).GetProperty("amount").GetDecimal());
        Assert.Equal(75m, (await app.Applied(itemId, now.AddHours(-1))).GetProperty("amount").GetDecimal());
        Assert.Equal(90m, (await app.Applied(itemId, now.AddHours(2))).GetProperty("amount").GetDecimal());
        Assert.True((await app.Applied(itemId)).GetProperty("hasValidStandardPrice").GetBoolean());
        await using var db = app.OpenDatabase();
        Assert.Equal("Inactive", (await db.Items.SingleAsync()).Availability);
    }

    [Fact]
    public async Task DailyScopeOverlapConsidersActualEffectiveUtcInterval()
    {
        using var app = await PriceTestApplication.Create();
        var itemId = await app.AddItem();
        var now = app.Clock.GetUtcNow();
        await app.CreatePrice(itemId, from: now.AddHours(-2), to: now.AddMinutes(-90),
            dailyStart: "14:00", dailyEnd: "15:00");
        await app.CreatePrice(itemId, 80m, from: now.AddHours(-2), to: now.AddMinutes(-90),
            dailyStart: "14:30", dailyEnd: "16:00");
        await app.CreatePrice(itemId, 70m, from: now, to: now.AddDays(1),
            dailyStart: "14:00", dailyEnd: "15:00");
        await app.CreatePrice(itemId, 60m, from: now, to: now.AddDays(1),
            dailyStart: "15:00", dailyEnd: "16:00");
        var conflict = await app.Client.PostAsJsonAsync("/api/prices", new
        {
            itemId,
            channel = "AllChannels",
            amount = 50m,
            effectiveFrom = now.ToString("O"),
            effectiveTo = now.AddDays(1).ToString("O"),
            dailyStart = "14:30",
            dailyEnd = "15:30"
        });
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
    }

    [Fact]
    public async Task StandardPriceMustBeCurrentlyEffectiveRatherThanMerelyPresent()
    {
        using var app = await PriceTestApplication.Create();
        var itemId = await app.AddItem();
        await app.CreatePrice(itemId, from: app.Clock.GetUtcNow().AddDays(1));
        await app.CreatePrice(itemId, 90m, "DineIn");
        Assert.False((await app.Applied(itemId)).GetProperty("hasValidStandardPrice").GetBoolean());
    }

    [Fact]
    public async Task ActiveAndHistoricalPricesCannotBeUpdatedOrDeleted()
    {
        using var app = await PriceTestApplication.Create();
        var itemId = await app.AddItem();
        var price = await app.CreatePrice(itemId);
        var id = price.GetProperty("priceId").GetGuid();
        Assert.Equal(HttpStatusCode.Conflict,
            (await app.Mutate(HttpMethod.Put, $"/api/prices/{id}", new
            {
                channel = "AllChannels",
                amount = 25m,
                effectiveFrom = app.Clock.GetUtcNow().AddDays(1).ToString("O")
            })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,
            (await app.Mutate(HttpMethod.Delete, $"/api/prices/{id}")).StatusCode);
    }

    [Fact]
    public async Task ReplacePreservesScopeAndCreatesOneAtomicTransition()
    {
        using var app = await PriceTestApplication.Create();
        var itemId = await app.AddItem();
        var current = await app.CreatePrice(itemId, 100m, "DineIn", dailyStart: "14:00", dailyEnd: "17:00");
        var id = current.GetProperty("priceId").GetGuid();
        var response = await app.Mutate(HttpMethod.Post, $"/api/prices/{id}/replace", new
            {
                amount = 125.50m,
                effectiveTo = app.Clock.GetUtcNow().AddDays(7).ToString("O")
            });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var replacement = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("result");
        Assert.NotEqual(id, replacement.GetProperty("priceId").GetGuid());
        Assert.Equal(itemId, replacement.GetProperty("itemId").GetGuid());
        Assert.Equal("DineIn", replacement.GetProperty("channel").GetString());
        Assert.Equal("14:00:00", replacement.GetProperty("dailyStart").GetString());
        Assert.Equal("17:00:00", replacement.GetProperty("dailyEnd").GetString());
        Assert.Equal(app.Clock.GetUtcNow(), replacement.GetProperty("effectiveFrom").GetDateTimeOffset());
        var old = await app.Client.GetFromJsonAsync<JsonElement>($"/api/prices/{id}");
        Assert.Equal(replacement.GetProperty("effectiveFrom").GetDateTimeOffset(),
            old.GetProperty("result").GetProperty("effectiveTo").GetDateTimeOffset());
        Assert.Equal(100m, (await app.Applied(itemId, app.Clock.GetUtcNow().AddSeconds(-1))).GetProperty("amount").GetDecimal());
        Assert.Equal(125.50m, (await app.Applied(itemId)).GetProperty("amount").GetDecimal());
        Assert.Equal(HttpStatusCode.Conflict,
            (await app.Mutate(HttpMethod.Post, $"/api/prices/{id}/replace", new { amount = 150m })).StatusCode);
        await using var db = app.OpenDatabase();
        Assert.Equal(2, await db.Prices.CountAsync());
        Assert.Equal("Inactive", (await db.Items.SingleAsync()).Availability);
    }

    [Fact]
    public async Task ConflictingReplacementDoesNotEndTheOldPrice()
    {
        using var app = await PriceTestApplication.Create();
        var itemId = await app.AddItem();
        var now = app.Clock.GetUtcNow();
        var current = await app.CreatePrice(itemId, to: now.AddDays(1));
        await app.CreatePrice(itemId, 200m, from: now.AddDays(1));
        var id = current.GetProperty("priceId").GetGuid();
        Assert.Equal(HttpStatusCode.Conflict,
            (await app.Mutate(HttpMethod.Post, $"/api/prices/{id}/replace", new { amount = 150m })).StatusCode);
        var detail = await app.Client.GetFromJsonAsync<JsonElement>($"/api/prices/{id}");
        Assert.Equal(current.GetProperty("effectiveTo").GetDateTimeOffset(),
            detail.GetProperty("result").GetProperty("effectiveTo").GetDateTimeOffset());
    }

    [Fact]
    public async Task ReplaceRejectsScopeChangesWithoutConditionalHeaders()
    {
        using var app = await PriceTestApplication.Create();
        var itemId = await app.AddItem();
        var price = await app.CreatePrice(itemId);
        var id = price.GetProperty("priceId").GetGuid();
        Assert.Equal(HttpStatusCode.BadRequest,
            (await app.Mutate(HttpMethod.Post, $"/api/prices/{id}/replace", new
            {
                amount = 10m,
                channel = "DineIn"
            })).StatusCode);
    }

    [Fact]
    public async Task ConcurrentCreatesAreRecheckedUnderThePerItemTransaction()
    {
        using var app = await PriceTestApplication.Create();
        var itemId = await app.AddItem();
        var body = new
        {
            itemId,
            channel = "AllChannels",
            amount = 100m,
            effectiveFrom = app.Clock.GetUtcNow().ToString("O")
        };
        var responses = await Task.WhenAll(
            app.Client.PostAsJsonAsync("/api/prices", body),
            app.Client.PostAsJsonAsync("/api/prices", body));
        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Created);
        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Conflict);
        await using var db = app.OpenDatabase();
        Assert.Single(await db.Prices.ToListAsync());
    }

    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData("revoked", HttpStatusCode.Unauthorized)]
    [InlineData("ordinary", HttpStatusCode.Forbidden)]
    public async Task ManagementRoutesRequirePermission(string? token, HttpStatusCode expected)
    {
        using var app = await PriceTestApplication.Create();
        var itemId = await app.AddItem();
        app.Client.DefaultRequestHeaders.Authorization = token is null
            ? null
            : new AuthenticationHeaderValue("Bearer", token);
        Assert.Equal(expected, (await app.Client.GetAsync("/api/prices")).StatusCode);
        Assert.Equal(expected, (await app.Client.GetAsync($"/api/items/{itemId}/price?channel=DineIn")).StatusCode);
        Assert.Equal(expected, (await app.Client.PostAsJsonAsync("/api/prices", new
        {
            itemId,
            channel = "AllChannels",
            amount = 10m,
            effectiveFrom = app.Clock.GetUtcNow().ToString("O")
        })).StatusCode);
    }
}
