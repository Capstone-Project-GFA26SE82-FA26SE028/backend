using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Catalog.Tests;

public sealed class RecipeFlowTests
{
    [Fact]
    public async Task CreateRecipe_ValidRequest_Returns201CreatedAndCalculatesSnapshot()
    {
        var inventory = new TestInventoryLookup();
        using var app = await ItemTestApplication.Create(inventoryLookup: inventory);
        var itemId = await OptionTestData.Item(app, "Beef Noodle");

        var ing1 = Guid.NewGuid();
        var unit1 = Guid.NewGuid();
        var ing2 = Guid.NewGuid();
        var unit2 = Guid.NewGuid();
        inventory.Factors[(ing2, unit2)] = 0.001m;

        var response = await app.Client.PostAsJsonAsync($"/api/items/{itemId}/recipes", new
        {
            instructions = "1. Cook broth. 2. Blanch noodles.",
            ingredients = new[]
            {
                new
                {
                    ingredientRef = ing1,
                    unitRef = unit1,
                    quantity = 200m,
                    conversionFactor = 1.0m
                },
                new
                {
                    ingredientRef = ing2,
                    unitRef = unit2,
                    quantity = 50m,
                    conversionFactor = 0.001m
                }
            },
            media = new[]
            {
                new
                {
                    mediaType = "image",
                    url = "https://example.com/step1.png",
                    displayOrder = 1
                }
            }
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(json.GetProperty("isSuccess").GetBoolean());

        var result = json.GetProperty("result");
        var recipeVersionId = result.GetProperty("recipeVersionId").GetGuid();
        Assert.NotEqual(Guid.Empty, recipeVersionId);
        Assert.Equal(1, result.GetProperty("version").GetInt32());
        Assert.Equal(itemId, result.GetProperty("itemId").GetGuid());
        Assert.Equal("1. Cook broth. 2. Blanch noodles.", result.GetProperty("instructions").GetString());

        var ingredients = result.GetProperty("ingredients");
        Assert.Equal(2, ingredients.GetArrayLength());

        var firstIng = ingredients[0];
        Assert.Equal(200m, firstIng.GetProperty("quantity").GetDecimal());
        Assert.Equal(200m, firstIng.GetProperty("baseQuantitySnapshot").GetDecimal());

        var secondIng = ingredients[1];
        Assert.Equal(50m, secondIng.GetProperty("quantity").GetDecimal());
        Assert.Equal(0.05m, secondIng.GetProperty("baseQuantitySnapshot").GetDecimal());

        var media = result.GetProperty("media");
        Assert.Equal(1, media.GetArrayLength());
        Assert.Equal("https://example.com/step1.png", media[0].GetProperty("url").GetString());

        Assert.Equal($"/api/recipes/{recipeVersionId}", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task CreateRecipe_SecondVersion_IncrementsVersionAndClosesPrevious()
    {
        using var app = await ItemTestApplication.Create();
        var itemId = await OptionTestData.Item(app, "Chicken Rice");

        var ing = Guid.NewGuid();
        var unit = Guid.NewGuid();
        var t1 = DateTime.UtcNow.AddHours(-2);
        var t2 = DateTime.UtcNow;

        var res1 = await app.Client.PostAsJsonAsync($"/api/items/{itemId}/recipes", new
        {
            effectiveFrom = t1,
            instructions = "Version 1 instructions",
            ingredients = new[]
            {
                new
                {
                    ingredientRef = ing,
                    unitRef = unit,
                    quantity = 100m,
                    conversionFactor = 1.0m
                }
            }
        });
        Assert.Equal(HttpStatusCode.Created, res1.StatusCode);
        var v1Result = (await res1.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("result");
        var v1Id = v1Result.GetProperty("recipeVersionId").GetGuid();
        Assert.Equal(1, v1Result.GetProperty("version").GetInt32());

        var res2 = await app.Client.PostAsJsonAsync($"/api/items/{itemId}/recipes", new
        {
            effectiveFrom = t2,
            instructions = "Version 2 instructions",
            ingredients = new[]
            {
                new
                {
                    ingredientRef = ing,
                    unitRef = unit,
                    quantity = 150m,
                    conversionFactor = 1.0m
                }
            }
        });
        Assert.Equal(HttpStatusCode.Created, res2.StatusCode);
        var v2Result = (await res2.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("result");
        Assert.Equal(2, v2Result.GetProperty("version").GetInt32());
        Assert.Equal(JsonValueKind.Null, v2Result.GetProperty("effectiveTo").ValueKind);

        // Fetch v1 to check effectiveTo was closed
        var v1Check = await app.Client.GetFromJsonAsync<JsonElement>($"/api/recipes/{v1Id}");
        var v1Detail = v1Check.GetProperty("result");
        Assert.Equal(JsonValueKind.String, v1Detail.GetProperty("effectiveTo").ValueKind);
    }

    [Fact]
    public async Task GetActiveRecipe_KitchenRole_Allowed_And_ReturnsActiveVersion()
    {
        using var app = await ItemTestApplication.Create();
        var itemId = await OptionTestData.Item(app, "Milk Tea");
        var ing = Guid.NewGuid();
        var unit = Guid.NewGuid();

        await app.Client.PostAsJsonAsync($"/api/items/{itemId}/recipes", new
        {
            instructions = "Brew tea for 10 minutes",
            ingredients = new[]
            {
                new
                {
                    ingredientRef = ing,
                    unitRef = unit,
                    quantity = 30m,
                    conversionFactor = 1.0m
                }
            }
        });

        // Switch token to Kitchen staff
        app.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "kitchen");

        var activeRes = await app.Client.GetAsync($"/api/items/{itemId}/recipes/active");
        Assert.Equal(HttpStatusCode.OK, activeRes.StatusCode);
        var activeJson = await activeRes.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Brew tea for 10 minutes", activeJson.GetProperty("result").GetProperty("instructions").GetString());

        // Kitchen cannot create recipe -> 403 Forbidden
        var forbiddenRes = await app.Client.PostAsJsonAsync($"/api/items/{itemId}/recipes", new
        {
            instructions = "Kitchen trying to modify",
            ingredients = new[]
            {
                new
                {
                    ingredientRef = ing,
                    unitRef = unit,
                    quantity = 40m,
                    conversionFactor = 1.0m
                }
            }
        });
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenRes.StatusCode);
    }

    [Fact]
    public async Task CreateRecipe_ValidationFailures_ReturnAppropriateError()
    {
        using var app = await ItemTestApplication.Create();
        var itemId = await OptionTestData.Item(app, "Fried Rice");
        var ing = Guid.NewGuid();
        var unit = Guid.NewGuid();

        // 1. Non-existent item
        var nonExistent = await app.Client.PostAsJsonAsync($"/api/items/{Guid.NewGuid()}/recipes", new
        {
            ingredients = new[]
            {
                new { ingredientRef = ing, unitRef = unit, quantity = 10m, conversionFactor = 1.0m }
            }
        });
        Assert.Equal(HttpStatusCode.NotFound, nonExistent.StatusCode);

        // 2. Empty ingredients
        var emptyIngredients = await app.Client.PostAsJsonAsync($"/api/items/{itemId}/recipes", new
        {
            ingredients = Array.Empty<object>()
        });
        Assert.Equal(HttpStatusCode.BadRequest, emptyIngredients.StatusCode);

        // 3. Zero quantity
        var zeroQty = await app.Client.PostAsJsonAsync($"/api/items/{itemId}/recipes", new
        {
            ingredients = new[]
            {
                new { ingredientRef = ing, unitRef = unit, quantity = 0m, conversionFactor = 1.0m }
            }
        });
        Assert.Equal(HttpStatusCode.BadRequest, zeroQty.StatusCode);

        // 4. Duplicate ingredient
        var dupIng = await app.Client.PostAsJsonAsync($"/api/items/{itemId}/recipes", new
        {
            ingredients = new[]
            {
                new { ingredientRef = ing, unitRef = unit, quantity = 10m, conversionFactor = 1.0m },
                new { ingredientRef = ing, unitRef = unit, quantity = 20m, conversionFactor = 1.0m }
            }
        });
        Assert.Equal(HttpStatusCode.BadRequest, dupIng.StatusCode);
    }

    [Fact]
    public async Task GetHistory_ReturnsAllVersions()
    {
        using var app = await ItemTestApplication.Create();
        var itemId = await OptionTestData.Item(app, "Pork Chop");
        var ing = Guid.NewGuid();
        var unit = Guid.NewGuid();

        await app.Client.PostAsJsonAsync($"/api/items/{itemId}/recipes", new
        {
            instructions = "Version 1",
            ingredients = new[] { new { ingredientRef = ing, unitRef = unit, quantity = 100m, conversionFactor = 1m } }
        });

        await app.Client.PostAsJsonAsync($"/api/items/{itemId}/recipes", new
        {
            instructions = "Version 2",
            ingredients = new[] { new { ingredientRef = ing, unitRef = unit, quantity = 120m, conversionFactor = 1m } }
        });

        var historyRes = await app.Client.GetFromJsonAsync<JsonElement>($"/api/items/{itemId}/recipes");
        var items = historyRes.GetProperty("result");
        Assert.Equal(2, items.GetArrayLength());
        Assert.Equal(2, items[0].GetProperty("version").GetInt32());
        Assert.Equal(1, items[1].GetProperty("version").GetInt32());
    }

    [Fact]
    public async Task CreateOptionRecipe_ValidRequest_Returns201CreatedAndCalculatesSnapshot()
    {
        using var app = await ItemTestApplication.Create();
        var groupId = await OptionTestData.Group(app, "Toppings");
        var optionId = await OptionTestData.Option(app, groupId, "Cheese Foam", 15000m);

        var ing1 = Guid.NewGuid();
        var unit1 = Guid.NewGuid();

        var response = await app.Client.PostAsJsonAsync($"/api/options/{optionId}/recipes", new
        {
            instructions = "1. Whip cream cheese. 2. Pour over tea.",
            ingredients = new[]
            {
                new
                {
                    ingredientRef = ing1,
                    unitRef = unit1,
                    quantity = 50m,
                    conversionFactor = 1.0m
                }
            },
            media = new[]
            {
                new
                {
                    mediaType = "image",
                    url = "https://example.com/cheese-foam.png",
                    displayOrder = 1
                }
            }
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(json.GetProperty("isSuccess").GetBoolean());

        var result = json.GetProperty("result");
        var recipeVersionId = result.GetProperty("recipeVersionId").GetGuid();
        Assert.NotEqual(Guid.Empty, recipeVersionId);
        Assert.Equal(1, result.GetProperty("version").GetInt32());
        Assert.Equal(optionId, result.GetProperty("optionId").GetGuid());
        Assert.Equal(JsonValueKind.Null, result.GetProperty("itemId").ValueKind);
        Assert.Equal("1. Whip cream cheese. 2. Pour over tea.", result.GetProperty("instructions").GetString());

        var ingredients = result.GetProperty("ingredients");
        Assert.Equal(1, ingredients.GetArrayLength());
        var firstIng = ingredients[0];
        Assert.Equal(50m, firstIng.GetProperty("quantity").GetDecimal());
        Assert.Equal(50m, firstIng.GetProperty("baseQuantitySnapshot").GetDecimal());

        var media = result.GetProperty("media");
        Assert.Equal(1, media.GetArrayLength());
        Assert.Equal("https://example.com/cheese-foam.png", media[0].GetProperty("url").GetString());

        Assert.Equal($"/api/recipes/{recipeVersionId}", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task CreateOptionRecipe_SecondVersion_IncrementsVersionAndClosesPrevious()
    {
        using var app = await ItemTestApplication.Create();
        var groupId = await OptionTestData.Group(app, "Sizes");
        var optionId = await OptionTestData.Option(app, groupId, "Size L", 10000m);

        var ing = Guid.NewGuid();
        var unit = Guid.NewGuid();
        var t1 = DateTime.UtcNow.AddHours(-2);
        var t2 = DateTime.UtcNow;

        var res1 = await app.Client.PostAsJsonAsync($"/api/options/{optionId}/recipes", new
        {
            effectiveFrom = t1,
            instructions = "Size L standard",
            ingredients = new[]
            {
                new
                {
                    ingredientRef = ing,
                    unitRef = unit,
                    quantity = 500m,
                    conversionFactor = 1.0m
                }
            }
        });
        Assert.Equal(HttpStatusCode.Created, res1.StatusCode);
        var v1Result = (await res1.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("result");
        var v1Id = v1Result.GetProperty("recipeVersionId").GetGuid();
        Assert.Equal(1, v1Result.GetProperty("version").GetInt32());

        var res2 = await app.Client.PostAsJsonAsync($"/api/options/{optionId}/recipes", new
        {
            effectiveFrom = t2,
            instructions = "Size L updated formulation",
            ingredients = new[]
            {
                new
                {
                    ingredientRef = ing,
                    unitRef = unit,
                    quantity = 700m,
                    conversionFactor = 1.0m
                }
            }
        });
        Assert.Equal(HttpStatusCode.Created, res2.StatusCode);
        var v2Result = (await res2.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("result");
        Assert.Equal(2, v2Result.GetProperty("version").GetInt32());
        Assert.Equal(JsonValueKind.Null, v2Result.GetProperty("effectiveTo").ValueKind);

        // Fetch v1 to check effectiveTo was closed
        var v1Check = await app.Client.GetFromJsonAsync<JsonElement>($"/api/recipes/{v1Id}");
        var v1Detail = v1Check.GetProperty("result");
        Assert.Equal(JsonValueKind.String, v1Detail.GetProperty("effectiveTo").ValueKind);
    }

    [Fact]
    public async Task GetActiveOptionRecipe_KitchenRole_Allowed_And_ReturnsActiveVersion()
    {
        using var app = await ItemTestApplication.Create();
        var groupId = await OptionTestData.Group(app, "Toppings");
        var optionId = await OptionTestData.Option(app, groupId, "Boba Pearl", 5000m);
        var ing = Guid.NewGuid();
        var unit = Guid.NewGuid();

        await app.Client.PostAsJsonAsync($"/api/options/{optionId}/recipes", new
        {
            instructions = "Scoop 1 spoonful of boba",
            ingredients = new[]
            {
                new
                {
                    ingredientRef = ing,
                    unitRef = unit,
                    quantity = 40m,
                    conversionFactor = 1.0m
                }
            }
        });

        // Switch token to Kitchen staff
        app.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "kitchen");

        var activeRes = await app.Client.GetAsync($"/api/options/{optionId}/recipes/active");
        Assert.Equal(HttpStatusCode.OK, activeRes.StatusCode);
        var activeJson = await activeRes.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Scoop 1 spoonful of boba", activeJson.GetProperty("result").GetProperty("instructions").GetString());

        // Kitchen cannot create option recipe -> 403 Forbidden
        var forbiddenRes = await app.Client.PostAsJsonAsync($"/api/options/{optionId}/recipes", new
        {
            instructions = "Kitchen modification attempt",
            ingredients = new[]
            {
                new
                {
                    ingredientRef = ing,
                    unitRef = unit,
                    quantity = 50m,
                    conversionFactor = 1.0m
                }
            }
        });
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenRes.StatusCode);
    }

    [Fact]
    public async Task CreateOptionRecipe_NonExistentOption_Returns404()
    {
        using var app = await ItemTestApplication.Create();
        var nonExistentOptionId = Guid.NewGuid();
        var ing = Guid.NewGuid();
        var unit = Guid.NewGuid();

        var response = await app.Client.PostAsJsonAsync($"/api/options/{nonExistentOptionId}/recipes", new
        {
            ingredients = new[]
            {
                new
                {
                    ingredientRef = ing,
                    unitRef = unit,
                    quantity = 25m,
                    conversionFactor = 1.0m
                }
            }
        });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetOptionRecipeHistory_ReturnsAllVersions()
    {
        using var app = await ItemTestApplication.Create();
        var groupId = await OptionTestData.Group(app, "Toppings");
        var optionId = await OptionTestData.Option(app, groupId, "Pudding", 8000m);
        var ing = Guid.NewGuid();
        var unit = Guid.NewGuid();

        await app.Client.PostAsJsonAsync($"/api/options/{optionId}/recipes", new
        {
            instructions = "Version 1 formulation",
            ingredients = new[] { new { ingredientRef = ing, unitRef = unit, quantity = 60m, conversionFactor = 1m } }
        });

        await app.Client.PostAsJsonAsync($"/api/options/{optionId}/recipes", new
        {
            instructions = "Version 2 formulation",
            ingredients = new[] { new { ingredientRef = ing, unitRef = unit, quantity = 80m, conversionFactor = 1m } }
        });

        var historyRes = await app.Client.GetFromJsonAsync<JsonElement>($"/api/options/{optionId}/recipes");
        var items = historyRes.GetProperty("result");
        Assert.Equal(2, items.GetArrayLength());
        Assert.Equal(2, items[0].GetProperty("version").GetInt32());
        Assert.Equal(1, items[1].GetProperty("version").GetInt32());
    }
}
