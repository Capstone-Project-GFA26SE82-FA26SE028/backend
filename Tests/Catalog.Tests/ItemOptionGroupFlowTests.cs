using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Catalog.Tests;

public sealed class ItemOptionGroupFlowTests
{
    [Fact]
    public async Task AssignUpdateAndRemoveUseBothKeysAndPreserveOtherItems()
    {
        using var app = await ItemTestApplication.Create();
        var group = await OptionTestData.Group(app, "Size");
        var first = await OptionTestData.Item(app, "Soup");
        var second = await OptionTestData.Item(app, "Rice");
        var created = await OptionTestData.Assign(app, first, group, 1, 1);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal($"/api/items/{first}/option-groups/{group}", created.Headers.Location?.OriginalString);
        Assert.Equal(HttpStatusCode.Created, (await OptionTestData.Assign(app, second, group, 0, 2)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await OptionTestData.Assign(app, first, group)).StatusCode);
        var list = (await app.Client.GetFromJsonAsync<JsonElement>($"/api/items/{first}/option-groups")).GetProperty("result");
        Assert.Equal(1, list.GetArrayLength());
        Assert.Equal("Size", list[0].GetProperty("optionGroupName").GetString());
        Assert.Equal(1, list[0].GetProperty("minSelection").GetInt32());

        Assert.Equal(HttpStatusCode.OK, (await app.Client.PutAsJsonAsync($"/api/items/{first}/option-groups/{group}", new
        {
            minSelection = 0,
            maxSelection = 3
        })).StatusCode);
        var unchanged = (await app.Client.GetFromJsonAsync<JsonElement>(
            $"/api/items/{second}/option-groups/{group}")).GetProperty("result");
        Assert.Equal(2, unchanged.GetProperty("maxSelection").GetInt32());
        var updated = (await app.Client.GetFromJsonAsync<JsonElement>(
            $"/api/items/{first}/option-groups/{group}")).GetProperty("result");
        Assert.Equal(3, updated.GetProperty("maxSelection").GetInt32());

        Assert.Equal(HttpStatusCode.OK, (await app.Client.DeleteAsync($"/api/items/{first}/option-groups/{group}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await app.Client.GetAsync($"/api/items/{first}/option-groups/{group}")).StatusCode);
        await using var db = app.OpenDatabase();
        Assert.Equal(second, (await db.ItemOptionGroups.SingleAsync()).ItemId);
        Assert.Equal(1, await db.OptionGroups.CountAsync());
        Assert.Equal(2, await db.Items.CountAsync());
    }

    [Theory]
    [InlineData(null, 1)]
    [InlineData(0, null)]
    [InlineData(-1, 1)]
    [InlineData(0, 0)]
    [InlineData(2, 1)]
    public async Task InvalidLimitsAreRejectedOnCreateAndUpdate(int? min, int? max)
    {
        using var app = await ItemTestApplication.Create();
        var group = await OptionTestData.Group(app);
        var item = await OptionTestData.Item(app);
        Assert.Equal(HttpStatusCode.BadRequest, (await app.Client.PostAsJsonAsync($"/api/items/{item}/option-groups", new
        {
            optionGroupId = group,
            minSelection = min,
            maxSelection = max
        })).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await OptionTestData.Assign(app, item, group)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await app.Client.PutAsJsonAsync($"/api/items/{item}/option-groups/{group}", new
        {
            minSelection = min,
            maxSelection = max
        })).StatusCode);
        await using var db = app.OpenDatabase();
        var link = await db.ItemOptionGroups.SingleAsync();
        Assert.Equal(0, link.MinSelection);
        Assert.Equal(1, link.MaxSelection);
    }

    [Fact]
    public async Task MissingResourcesAndWrongCompositeKeysDoNotChangeAssignments()
    {
        using var app = await ItemTestApplication.Create();
        var group = await OptionTestData.Group(app);
        var item = await OptionTestData.Item(app);
        var unknown = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.NotFound, (await OptionTestData.Assign(app, unknown, group)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await OptionTestData.Assign(app, item, unknown)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await app.Client.GetAsync($"/api/items/{unknown}/option-groups")).StatusCode);
        var empty = (await app.Client.GetFromJsonAsync<JsonElement>($"/api/items/{item}/option-groups")).GetProperty("result");
        Assert.Equal(0, empty.GetArrayLength());
        Assert.Equal(HttpStatusCode.Created, (await OptionTestData.Assign(app, item, group)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await app.Client.DeleteAsync($"/api/items/{unknown}/option-groups/{group}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await app.Client.PutAsJsonAsync($"/api/items/{item}/option-groups/{unknown}", new
            {
                minSelection = 0,
                maxSelection = 1
            })).StatusCode);
        await using var db = app.OpenDatabase();
        Assert.Equal(1, await db.ItemOptionGroups.CountAsync());
    }

    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData("revoked", HttpStatusCode.Unauthorized)]
    [InlineData("disabled", HttpStatusCode.Unauthorized)]
    [InlineData("waiter", HttpStatusCode.Forbidden)]
    [InlineData("kitchen", HttpStatusCode.Forbidden)]
    public async Task EveryOptionManagementEndpointRequiresManagerPermission(string? token, HttpStatusCode expected)
    {
        using var app = await ItemTestApplication.Create();
        var group = await OptionTestData.Group(app);
        var option = await OptionTestData.Option(app, group);
        var item = await OptionTestData.Item(app);
        Assert.Equal(HttpStatusCode.Created, (await OptionTestData.Assign(app, item, group)).StatusCode);
        app.Client.DefaultRequestHeaders.Authorization = token is null
            ? null
            : new AuthenticationHeaderValue("Bearer", token);
        var requests = new (HttpMethod Method, string Route, object? Body)[]
        {
            (HttpMethod.Get, "/api/option-groups", null),
            (HttpMethod.Get, $"/api/option-groups/{group}", null),
            (HttpMethod.Post, "/api/option-groups", new
            {
                optionGroupName = "Unauthorized"
            }),
            (HttpMethod.Put, $"/api/option-groups/{group}", new
            {
                optionGroupName = "Unauthorized"
            }),
            (HttpMethod.Delete, $"/api/option-groups/{group}", null),
            (HttpMethod.Get, "/api/options", null),
            (HttpMethod.Get, $"/api/options/{option}", null),
            (HttpMethod.Post, "/api/options", new
            {
                optionGroupId = group,
                optionName = "Unauthorized",
                extraPrice = 0
            }),
            (HttpMethod.Put, $"/api/options/{option}", new
            {
                optionName = "Unauthorized",
                extraPrice = 0
            }),
            (HttpMethod.Patch, $"/api/options/{option}/status", new
            {
                availability = "Available"
            }),
            (HttpMethod.Delete, $"/api/options/{option}", null),
            (HttpMethod.Get, $"/api/items/{item}/option-groups", null),
            (HttpMethod.Get, $"/api/items/{item}/option-groups/{group}", null),
            (HttpMethod.Post, $"/api/items/{item}/option-groups", new
            {
                optionGroupId = group,
                minSelection = 0,
                maxSelection = 1
            }),
            (HttpMethod.Put, $"/api/items/{item}/option-groups/{group}", new
            {
                minSelection = 0,
                maxSelection = 5
            }),
            (HttpMethod.Delete, $"/api/items/{item}/option-groups/{group}", null)
        };
        foreach (var (method, route, body) in requests)
        {
            using var request = new HttpRequestMessage(method, route);
            if (body is not null)
            {
                request.Content = JsonContent.Create(body);
            }

            var response = await app.Client.SendAsync(request);
            Assert.True(response.StatusCode == expected, $"{method} {route}: {response.StatusCode}");
        }

        await using var db = app.OpenDatabase();
        Assert.Equal("Topping", (await db.OptionGroups.SingleAsync()).OptionGroupName);
        Assert.Equal("Inactive", (await db.Options.SingleAsync()).Availability);
        Assert.Equal(1, (await db.ItemOptionGroups.SingleAsync()).MaxSelection);
    }
}
