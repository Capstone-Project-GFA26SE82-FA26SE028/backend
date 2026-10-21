using System.Net;
using System.Net.Http.Headers;
using Xunit;

namespace Catalog.Tests;

public sealed class RecipeAuthorizationTests
{
    [Theory]
    [InlineData(-2, -1, 403)]
    [InlineData(1, 2, 403)]
    [InlineData(-1, 1, 200)]
    public async Task KitchenCanReadOnlyCurrentlyEffectiveVersionById(int from, int to, int status)
    {
        using var app = await ItemTestApplication.Create();
        var item = await OptionTestData.Item(app, "Recipe access");
        var recipe = await app.AddRecipe(item, DateTime.UtcNow.AddDays(from), DateTime.UtcNow.AddDays(to));
        app.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "kitchen");
        Assert.Equal(status, (int)(await app.Client.GetAsync($"/api/recipes/{recipe}")).StatusCode);
        app.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "manager");
        Assert.Equal(HttpStatusCode.OK, (await app.Client.GetAsync($"/api/recipes/{recipe}")).StatusCode);
    }

    [Fact]
    public async Task KitchenCannotUseAtToReadFutureRecipe()
    {
        using var app = await ItemTestApplication.Create();
        var item = await OptionTestData.Item(app, "Scheduled recipe");
        await app.AddRecipe(item, DateTime.UtcNow.AddDays(1));
        app.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "kitchen");
        var at = Uri.EscapeDataString(DateTime.UtcNow.AddDays(2).ToString("O"));
        Assert.Equal(HttpStatusCode.Forbidden, (await app.Client.GetAsync($"/api/items/{item}/recipes/active?at={at}")).StatusCode);
    }
    [Fact]
    public async Task KitchenCannotQueryOptionRecipeAtArbitraryTime()
    {
        using var app = await ItemTestApplication.Create();
        var option = await OptionTestData.Option(app, await OptionTestData.Group(app));
        app.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "kitchen");
        var at = Uri.EscapeDataString(DateTime.UtcNow.AddDays(2).ToString("O"));
        Assert.Equal(HttpStatusCode.Forbidden, (await app.Client.GetAsync($"/api/options/{option}/recipes/active?at={at}")).StatusCode);
    }

}
