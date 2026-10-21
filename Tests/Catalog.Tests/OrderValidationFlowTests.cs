using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Catalog.Common.DTOs;
using Catalog.DAL.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Catalog.Tests;

public sealed class OrderValidationFlowTests
{
    private const string Endpoint = "/api/menu/validate-order-items";

    [Fact]
    public async Task ValidBatch_ReturnsServerSnapshots_AndKeepsDistinctLinesForSameItem()
    {
        using var app = await ItemTestApplication.Create();
        var data = await Seed(app);
        var request = Request(data);
        request.Items.Add(new OrderSelectionDTO
        {
            ItemId = data.ItemId,
            Quantity = 1,
            Options =
            [
                new OrderOptionSelectionDTO
                {
                    OptionId = data.OptionId,
                    Quantity = 2
                }
            ]
        });
        app.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "waiter");
        var result = await Result(await app.Client.PostAsJsonAsync(Endpoint, request), HttpStatusCode.OK);
        Assert.Equal(210000m, result.GetProperty("totalAmount").GetDecimal());
        Assert.Equal(ItemTestApplication.Now, result.GetProperty("evaluatedAt").GetDateTimeOffset());
        var lines = result.GetProperty("items");
        Assert.Equal(2, lines.GetArrayLength());
        Assert.Equal(0, lines[0].GetProperty("lineIndex").GetInt32());
        Assert.Equal(1, lines[1].GetProperty("lineIndex").GetInt32());
        Assert.Equal("Beef soup", lines[0].GetProperty("nameSnapshot").GetString());
        Assert.Equal(50000m, lines[0].GetProperty("unitPriceSnapshot").GetDecimal());
        Assert.Equal(15000m, lines[0].GetProperty("optionsTotalPerUnit").GetDecimal());
        Assert.Equal(65000m, lines[0].GetProperty("unitTotal").GetDecimal());
        Assert.Equal(130000m, lines[0].GetProperty("lineTotal").GetDecimal());
        Assert.Equal(data.RecipeId, lines[0].GetProperty("recipeVersionId").GetGuid());
        var option = lines[0].GetProperty("options")[0];
        Assert.Equal("Extra beef", option.GetProperty("nameSnapshot").GetString());
        Assert.Equal(data.OptionRecipeId, option.GetProperty("recipeVersionId").GetGuid());
        Assert.Equal(1, option.GetProperty("quantity").GetInt32());
        Assert.Empty(result.GetProperty("errors").EnumerateArray());
    }

    [Fact]
    public async Task Guest_UsesSessionCookie_AndInvalidSessionCannotValidate()
    {
        using var app = await ItemTestApplication.Create();
        var data = await Seed(app);
        app.Client.DefaultRequestHeaders.Authorization = null;
        var url = $"{Endpoint}?tableSessionId={MenuGuestClient.SessionId}";
        Assert.Equal(HttpStatusCode.Unauthorized, (await app.Client.PostAsJsonAsync(url, Request(data))).StatusCode);
        app.Client.DefaultRequestHeaders.Add("Cookie", $"guest_access_{MenuGuestClient.SessionId:N}={MenuGuestClient.Token}");
        Assert.Equal(HttpStatusCode.OK, (await app.Client.PostAsJsonAsync(url, Request(data))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await app.Client.PostAsJsonAsync($"{Endpoint}?tableSessionId={Guid.NewGuid()}", Request(data))).StatusCode);
    }

    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData("customer", HttpStatusCode.Forbidden)]
    [InlineData("kitchen", HttpStatusCode.Forbidden)]
    [InlineData("revoked", HttpStatusCode.Unauthorized)]
    [InlineData("disabled", HttpStatusCode.Unauthorized)]
    public async Task UnauthorizedActors_CannotValidate(string? token, HttpStatusCode expected)
    {
        using var app = await ItemTestApplication.Create();
        app.Client.DefaultRequestHeaders.Authorization = token is null ? null : new AuthenticationHeaderValue("Bearer", token);
        var request = new ValidateOrderItemsDTO
        {
            Items =
            [
                new OrderSelectionDTO
                {
                    ItemId = Guid.NewGuid(),
                    Quantity = 1
                }
            ]
        };
        Assert.Equal(expected, (await app.Client.PostAsJsonAsync(Endpoint, request)).StatusCode);
    }

    [Theory]
    [InlineData("inactive", "ItemUnavailable")]
    [InlineData("out-of-stock", "ItemOutOfStock")]
    [InlineData("category", "ItemUnavailable")]
    [InlineData("option-inactive", "OptionUnavailable")]
    [InlineData("option-out-of-stock", "OptionUnavailable")]
    [InlineData("missing-price", "PriceUnavailable")]
    [InlineData("conflicting-price", "PriceUnavailable")]
    [InlineData("missing-recipe", "RecipeUnavailable")]
    [InlineData("future-recipe", "RecipeUnavailable")]
    [InlineData("expired-recipe", "RecipeUnavailable")]
    [InlineData("conflicting-recipe", "RecipeUnavailable")]
    [InlineData("conflicting-option-recipe", "OptionRecipeConflict")]
    public async Task ChangedCatalog_ReturnsConflictWithLineError(string scenario, string code)
    {
        using var app = await ItemTestApplication.Create();
        var data = await Seed(app, withRecipe: scenario != "missing-recipe");
        await using (var db = app.OpenDatabase())
        {
            var item = await db.Items.Include(value => value.Category).SingleAsync();
            var option = await db.Options.SingleAsync();
            switch (scenario)
            {
                case "inactive":
                    item.Availability = "Inactive";
                    break;
                case "out-of-stock":
                    item.Availability = "OutOfStock";
                    break;
                case "category":
                    item.Category.IsActive = false;
                    break;
                case "option-inactive":
                    option.Availability = "Inactive";
                    break;
                case "option-out-of-stock":
                    option.Availability = "OutOfStock";
                    break;
                case "missing-price":
                    db.Prices.RemoveRange(await db.Prices.ToListAsync());
                    break;
                case "conflicting-price":
                    db.Prices.Add(Price(data.ItemId, 60000m));
                    break;
                case "future-recipe":
                    (await db.RecipeVersions.SingleAsync(value => value.ItemId == data.ItemId)).EffectiveFrom =
                        ItemTestApplication.Now.AddSeconds(1).UtcDateTime;
                    break;
                case "expired-recipe":
                    (await db.RecipeVersions.SingleAsync(value => value.ItemId == data.ItemId)).EffectiveTo =
                        ItemTestApplication.Now.UtcDateTime;
                    break;
                case "conflicting-option-recipe":
                    db.RecipeVersions.Add(OptionRecipe(data.OptionId));
                    break;
            }

            await db.SaveChangesAsync();
        }

        if (scenario == "conflicting-recipe")
        {
            await app.AddRecipe(data.ItemId);
        }

        var result = await Result(await app.Client.PostAsJsonAsync(Endpoint, Request(data)), HttpStatusCode.Conflict);
        Assert.Equal(code, result.GetProperty("errors")[0].GetProperty("code").GetString());
        Assert.Equal(0, result.GetProperty("errors")[0].GetProperty("lineIndex").GetInt32());
        Assert.Equal(JsonValueKind.Null, result.GetProperty("totalAmount").ValueKind);
        Assert.Empty(result.GetProperty("items").EnumerateArray());
    }

    [Theory]
    [InlineData("not-linked", HttpStatusCode.Conflict, "OptionNotAllowed")]
    [InlineData("required", HttpStatusCode.Conflict, "SelectionLimit")]
    [InlineData("above-max", HttpStatusCode.Conflict, "SelectionLimit")]
    [InlineData("duplicate-option", HttpStatusCode.BadRequest, null)]
    [InlineData("zero-item-quantity", HttpStatusCode.BadRequest, null)]
    [InlineData("negative-option-quantity", HttpStatusCode.BadRequest, null)]
    [InlineData("large-item-quantity", HttpStatusCode.BadRequest, null)]
    [InlineData("empty", HttpStatusCode.BadRequest, null)]
    [InlineData("too-many-lines", HttpStatusCode.BadRequest, null)]
    [InlineData("unknown-channel", HttpStatusCode.BadRequest, null)]
    public async Task InvalidSelections_AreRejected(string scenario, HttpStatusCode expected, string? code)
    {
        using var app = await ItemTestApplication.Create();
        var data = await Seed(app);
        var request = Request(data);
        switch (scenario)
        {
            case "not-linked":
                request.Items[0].Options[0].OptionId = Guid.NewGuid();
                break;
            case "required":
                request.Items[0].Options.Clear();
                break;
            case "above-max":
                request.Items[0].Options[0].Quantity = 3;
                break;
            case "duplicate-option":
                request.Items[0].Options.Add(request.Items[0].Options[0]);
                break;
            case "zero-item-quantity":
                request.Items[0].Quantity = 0;
                break;
            case "negative-option-quantity":
                request.Items[0].Options[0].Quantity = -1;
                break;
            case "large-item-quantity":
                request.Items[0].Quantity = 1001;
                break;
            case "empty":
                request.Items.Clear();
                break;
            case "too-many-lines":
                request.Items = Enumerable.Repeat(request.Items[0], 101).ToList();
                break;
            case "unknown-channel":
                request.Channel = "Unknown";
                break;
        }

        var response = await app.Client.PostAsJsonAsync(Endpoint, request);
        Assert.Equal(expected, response.StatusCode);
        if (code is not null)
        {
            var result = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("result");
            Assert.Equal(code, result.GetProperty("errors")[0].GetProperty("code").GetString());
        }
    }

    [Fact]
    public async Task ClientPricesAreNotAccepted()
    {
        using var app = await ItemTestApplication.Create();
        var data = await Seed(app);
        var response = await app.Client.PostAsJsonAsync(Endpoint, new
        {
            channel = "DineIn",
            items = new[]
            {
                new
                {
                    itemId = data.ItemId,
                    quantity = 1,
                    unitPriceSnapshot = 1m
                }
            }
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task OptionWithoutRecipe_IsAllowed_AndFailuresDoNotReturnPartialQuotes()
    {
        using var app = await ItemTestApplication.Create();
        var data = await Seed(app, withOptionRecipe: false);
        var request = Request(data);
        var success = await Result(await app.Client.PostAsJsonAsync(Endpoint, request), HttpStatusCode.OK);
        Assert.Equal(JsonValueKind.Null, success.GetProperty("items")[0].GetProperty("options")[0].GetProperty("recipeVersionId").ValueKind);
        request.Items.Add(new OrderSelectionDTO
        {
            ItemId = Guid.NewGuid(),
            Quantity = 1
        });
        var failure = await Result(await app.Client.PostAsJsonAsync(Endpoint, request), HttpStatusCode.Conflict);
        Assert.Empty(failure.GetProperty("items").EnumerateArray());
        Assert.Equal(1, failure.GetProperty("errors")[0].GetProperty("lineIndex").GetInt32());
        Assert.Equal("ItemUnavailable", failure.GetProperty("errors")[0].GetProperty("code").GetString());
    }

    [Fact]
    public async Task ValidationRechecksCurrentPrice_UsesSamePriceAsMenu()
    {
        using var app = await ItemTestApplication.Create();
        var data = await Seed(app);
        await using (var db = app.OpenDatabase())
        {
            var special = Price(data.ItemId, 40000m);
            special.Channel = "DineIn";
            special.DailyStart = new TimeOnly(15, 0);
            special.DailyEnd = new TimeOnly(16, 0);
            db.Prices.Add(special);
            await db.SaveChangesAsync();
        }

        var quote = await Result(await app.Client.PostAsJsonAsync(Endpoint, Request(data)), HttpStatusCode.OK);
        var menu = await Result(await app.Client.GetAsync($"/api/menu/{data.ItemId}"), HttpStatusCode.OK);
        Assert.Equal(menu.GetProperty("price").GetDecimal(), quote.GetProperty("items")[0].GetProperty("unitPriceSnapshot").GetDecimal());
        Assert.Equal(110000m, quote.GetProperty("totalAmount").GetDecimal());
        await using (var db = app.OpenDatabase())
        {
            (await db.Prices.SingleAsync(price => price.Channel == "DineIn")).Amount = 45000m;
            await db.SaveChangesAsync();
        }

        var updated = await Result(await app.Client.PostAsJsonAsync(Endpoint, Request(data)), HttpStatusCode.OK);
        Assert.Equal(120000m, updated.GetProperty("totalAmount").GetDecimal());
    }

    private static async Task<JsonElement> Result(HttpResponseMessage response, HttpStatusCode expected)
    {
        Assert.Equal(expected, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("result");
    }

    private static ValidateOrderItemsDTO Request(TestData data)
    {
        return new ValidateOrderItemsDTO
        {
            Items =
            [
                new OrderSelectionDTO
                {
                    ItemId = data.ItemId,
                    Quantity = 2,
                    Options =
                    [
                        new OrderOptionSelectionDTO
                        {
                            OptionId = data.OptionId,
                            Quantity = 1
                        }
                    ]
                }
            ]
        };
    }

    private static async Task<TestData> Seed(ItemTestApplication app, bool withRecipe = true, bool withOptionRecipe = true)
    {
        var item = await app.CreateItem(await app.AddCategory(), "Beef soup");
        var id = item.GetProperty("itemId").GetGuid();
        var recipeId = withRecipe ? await app.AddRecipe(id) : Guid.Empty;
        await using var db = app.OpenDatabase();
        (await db.Items.SingleAsync()).Availability = "Available";
        db.Prices.Add(Price(id, 50000m));
        var option = new Option
        {
            OptionId = Guid.NewGuid(),
            OptionName = "Extra beef",
            ExtraPrice = 15000m,
            Availability = "Available"
        };
        db.ItemOptionGroups.Add(new ItemOptionGroup
        {
            ItemId = id,
            MinSelection = 1,
            MaxSelection = 2,
            OptionGroup = new OptionGroup
            {
                OptionGroupId = Guid.NewGuid(),
                OptionGroupName = "Extra",
                Options = new List<Option>
                {
                    option
                }
            }
        });
        var optionRecipe = OptionRecipe(option.OptionId);
        if (withOptionRecipe)
        {
            db.RecipeVersions.Add(optionRecipe);
        }

        await db.SaveChangesAsync();
        return new TestData(id, option.OptionId, recipeId, optionRecipe.RecipeVersionId);
    }

    private static Price Price(Guid itemId, decimal amount)
    {
        return new Price
        {
            PriceId = Guid.NewGuid(),
            ItemId = itemId,
            Amount = amount,
            Channel = "AllChannels",
            EffectiveFrom = ItemTestApplication.Now.AddDays(-1).UtcDateTime
        };
    }

    private static RecipeVersion OptionRecipe(Guid optionId)
    {
        return new RecipeVersion
        {
            RecipeVersionId = Guid.NewGuid(),
            OptionId = optionId,
            Version = 1,
            EffectiveFrom = ItemTestApplication.Now.AddDays(-1).UtcDateTime,
            Instructions = "Add beef.",
            RecipeIngredients = new List<RecipeIngredient>
            {
                new RecipeIngredient
                {
                    RecipeIngredientId = Guid.NewGuid(),
                    IngredientRef = Guid.NewGuid(),
                    UnitRef = Guid.NewGuid(),
                    Quantity = 50m,
                    ConversionFactorSnapshot = 0.001m,
                    BaseQuantitySnapshot = 0.05m
                }
            }
        };
    }

    private sealed record TestData(Guid ItemId, Guid OptionId, Guid RecipeId, Guid OptionRecipeId);
}
