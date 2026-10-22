using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Catalog.Tests;

public sealed class RecipeInventoryTests
{
    [Fact]
    public async Task ServerConversionOverridesClientAndOldSnapshotDoesNotChange()
    {
        var lookup = new TestInventoryLookup();
        using var app = await ItemTestApplication.Create(inventoryLookup: lookup);
        var item = await OptionTestData.Item(app, "Rice");
        var ingredient = Guid.NewGuid();
        var unit = Guid.NewGuid();
        lookup.Factors[(ingredient, unit)] = 1000;
        var response = await app.Client.PostAsJsonAsync($"/api/items/{item}/recipes", new
        {
            ingredients = new[]
            {
                new
                {
                    ingredientRef = ingredient,
                    unitRef = unit,
                    quantity = 0.15m,
                    conversionFactor = 999m
                }
            }
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        lookup.Factors[(ingredient, unit)] = 2000;
        await using var db = app.OpenDatabase();
        var saved = await db.RecipeIngredients.SingleAsync();
        Assert.Equal(1000, saved.ConversionFactorSnapshot);
        Assert.Equal(150, saved.BaseQuantitySnapshot);
    }

    [Theory]
    [InlineData(404)]
    [InlineData(409)]
    [InlineData(503)]
    [InlineData(504)]
    public async Task InventoryFailureDoesNotSaveRecipe(int status)
    {
        var lookup = new TestInventoryLookup
        {
            FailureStatus = status
        };
        using var app = await ItemTestApplication.Create(inventoryLookup: lookup);
        var item = await OptionTestData.Item(app, "Rice");
        var response = await app.Client.PostAsJsonAsync($"/api/items/{item}/recipes", new
        {
            ingredients = new[]
            {
                new
                {
                    ingredientRef = Guid.NewGuid(),
                    unitRef = Guid.NewGuid(),
                    quantity = 1m
                }
            }
        });
        Assert.Equal(status, (int)response.StatusCode);
        await using var db = app.OpenDatabase();
        Assert.Empty(await db.RecipeVersions.ToListAsync());
    }
}
