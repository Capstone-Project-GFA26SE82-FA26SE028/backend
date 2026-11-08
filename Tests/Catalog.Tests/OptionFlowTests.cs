using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Catalog.DAL.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Catalog.Tests;

public sealed class OptionFlowTests
{
    [Fact]
    public async Task CrudAndStatusPreserveGroupAndUseDecimalExtraPrice()
    {
        using var app = await ItemTestApplication.Create();
        var group = await OptionTestData.Group(app);
        var id = await OptionTestData.Option(app, group, " Extra egg ", 10000.25m);
        var detail = (await app.Client.GetFromJsonAsync<JsonElement>($"/api/options/{id}")).GetProperty("result");
        Assert.Equal("Extra egg", detail.GetProperty("optionName").GetString());
        Assert.Equal(HttpStatusCode.OK, (await app.Client.PutAsJsonAsync($"/api/options/{id}", new
        {
            optionName = "Two eggs",
            extraPrice = 20000m
        })).StatusCode);
        foreach (var availability in new[] { "Available", "OutOfStock", "Inactive" })
        {
            var response = await app.Client.PatchAsJsonAsync($"/api/options/{id}/status", new
            {
                availability
            });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var result = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("result");
            Assert.Equal(availability, result.GetProperty("availability").GetString());
            Assert.Equal(20000m, result.GetProperty("extraPrice").GetDecimal());
            Assert.Equal(group, result.GetProperty("optionGroupId").GetGuid());
        }

        Assert.Equal(HttpStatusCode.OK, (await app.Client.DeleteAsync($"/api/options/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await app.Client.GetAsync($"/api/options/{id}")).StatusCode);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0.001)]
    [InlineData(10000000000000000)]
    public async Task InvalidPricesAreRejected(double price)
    {
        using var app = await ItemTestApplication.Create();
        var group = await OptionTestData.Group(app);
        Assert.Equal(HttpStatusCode.BadRequest, (await app.Client.PostAsJsonAsync("/api/options", new
        {
            optionGroupId = group,
            optionName = "Egg",
            extraPrice = price
        })).StatusCode);
    }

    [Fact]
    public async Task ZeroPriceIsValidButMissingPriceAndInvalidNamesAreRejected()
    {
        using var app = await ItemTestApplication.Create();
        var group = await OptionTestData.Group(app);
        await OptionTestData.Option(app, group, "No ice", 0);
        Assert.Equal(HttpStatusCode.BadRequest, (await app.Client.PostAsJsonAsync("/api/options", new
        {
            optionGroupId = group,
            optionName = "Missing price"
        })).StatusCode);
        foreach (var name in new[] { "", "  ", "bad\nname", new string('x', 256) })
        {
            Assert.Equal(HttpStatusCode.BadRequest, (await app.Client.PostAsJsonAsync("/api/options", new
            {
                optionGroupId = group,
                optionName = name,
                extraPrice = 0
            })).StatusCode);
        }
    }

    [Fact]
    public async Task NamesAreUniqueWithinGroupAndListsFilterByGroupSearchAndStatus()
    {
        using var app = await ItemTestApplication.Create();
        var first = await OptionTestData.Group(app, "First");
        var second = await OptionTestData.Group(app, "Second");
        var id = await OptionTestData.Option(app, first, "Egg");
        await OptionTestData.Option(app, second, "Egg");
        var other = await OptionTestData.Option(app, first, "Cheese");
        Assert.Equal(HttpStatusCode.Conflict, (await app.Client.PostAsJsonAsync("/api/options", new
        {
            optionGroupId = first,
            optionName = " Egg ",
            extraPrice = 0
        })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await app.Client.PutAsJsonAsync($"/api/options/{other}", new
        {
            optionName = "Egg",
            extraPrice = 0
        })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await app.Client.PatchAsJsonAsync($"/api/options/{id}/status", new
        {
            availability = "Available"
        })).StatusCode);
        var page = (await app.Client.GetFromJsonAsync<JsonElement>(
            $"/api/options?optionGroupId={first}&search=Egg&availability=Available&pageSize=1")).GetProperty("result");
        Assert.Equal(1, page.GetProperty("totalCount").GetInt32());
        Assert.Equal(id, page.GetProperty("items")[0].GetProperty("optionId").GetGuid());
    }

    [Fact]
    public async Task RecipesAndAssignedGroupsPreventDeletionButAllowDisabling()
    {
        using var app = await ItemTestApplication.Create();
        var group = await OptionTestData.Group(app);
        var withRecipe = await OptionTestData.Option(app, group, "Recipe option");
        await using (var db = app.OpenDatabase())
        {
            db.RecipeVersions.Add(new RecipeVersion
            {
                RecipeVersionId = Guid.NewGuid(),
                OptionId = withRecipe,
                Version = 1,
                EffectiveFrom = ItemTestApplication.Now.UtcDateTime,
                Instructions = "Test recipe"
            });
            await db.SaveChangesAsync();
        }

        Assert.Equal(HttpStatusCode.Conflict, (await app.Client.DeleteAsync($"/api/options/{withRecipe}")).StatusCode);
        var assigned = await OptionTestData.Option(app, group, "Assigned option");
        Assert.Equal(HttpStatusCode.Created,
            (await OptionTestData.Assign(app, await OptionTestData.Item(app), group)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await app.Client.DeleteAsync($"/api/options/{assigned}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await app.Client.PatchAsJsonAsync($"/api/options/{assigned}/status", new
        {
            availability = "Inactive"
        })).StatusCode);
        await using var verification = app.OpenDatabase();
        Assert.Equal(2, await verification.Options.CountAsync());
    }

    [Fact]
    public async Task MissingGroupInvalidIdsStatusAndQueriesAreRejected()
    {
        using var app = await ItemTestApplication.Create();
        Assert.Equal(HttpStatusCode.NotFound, (await app.Client.PostAsJsonAsync("/api/options", new
        {
            optionGroupId = Guid.NewGuid(),
            optionName = "Egg",
            extraPrice = 0
        })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await app.Client.GetAsync($"/api/options?optionGroupId={Guid.NewGuid()}")).StatusCode);
        foreach (var route in new[] { "/api/options/not-a-guid", $"/api/options/{Guid.Empty}",
            "/api/options?page=0", "/api/options?pageSize=101", $"/api/options?optionGroupId={Guid.Empty}" })
        {
            Assert.Equal(HttpStatusCode.BadRequest, (await app.Client.GetAsync(route)).StatusCode);
        }

        var id = await OptionTestData.Option(app, await OptionTestData.Group(app));
        Assert.Equal(HttpStatusCode.BadRequest, (await app.Client.PatchAsJsonAsync($"/api/options/{id}/status", new
        {
            availability = "unknown"
        })).StatusCode);
    }
}
