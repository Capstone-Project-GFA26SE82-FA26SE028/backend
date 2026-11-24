using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Catalog.DAL.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Catalog.Tests;

public sealed class RecipeMediaFlowTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UploadAndSaveMediaWorksForBothItemAndOptionRecipes(bool option)
    {
        using var app = await ItemTestApplication.Create();
        using var image = await Upload(app, Png());
        Assert.Equal(HttpStatusCode.OK, image.StatusCode);
        var media = (await image.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("result");
        Assert.Equal("image", media.GetProperty("mediaType").GetString());
        var id = option ? await OptionTestData.Option(app, await OptionTestData.Group(app))
            : await OptionTestData.Item(app, "Recipe photo");
        using var created = await CreateRecipe(app, option, id, "image", media.GetProperty("url").GetString()!);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var recipe = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("result");
        Assert.Equal(media.GetProperty("url").GetString(), recipe.GetProperty("media")[0].GetProperty("url").GetString());
        await using var db = app.OpenDatabase();
        Assert.Single(await db.RecipeMedia.ToListAsync());
    }

    [Fact]
    public async Task VideoLargerThanDefaultGrpcLimitCanReachCloud()
    {
        var storage = new TestRecipeMediaStorage();
        using var app = await ItemTestApplication.Create(recipeMediaStorage: storage);
        var video = new byte[5 * 1024 * 1024];
        "ftyp"u8.CopyTo(video.AsSpan(4));
        using var response = await Upload(app, video);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("result");
        Assert.Equal("video", result.GetProperty("mediaType").GetString());
        Assert.Contains("/video/upload/", result.GetProperty("url").GetString());
        Assert.Equal(1, storage.Calls);
    }

    [Theory]
    [InlineData(null, 401)]
    [InlineData("waiter", 403)]
    [InlineData("kitchen", 403)]
    [InlineData("customer", 403)]
    [InlineData("disabled", 401)]
    public async Task UploadRequiresAnActiveMenuManager(string? token, int expected)
    {
        var storage = new TestRecipeMediaStorage();
        using var app = await ItemTestApplication.Create(recipeMediaStorage: storage);
        app.Client.DefaultRequestHeaders.Authorization = token is null ? null : new AuthenticationHeaderValue("Bearer", token);
        using var response = await Upload(app, Png());
        Assert.Equal(expected, (int)response.StatusCode);
        Assert.Equal(0, storage.Calls);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(32)]
    [InlineData(20971521)]
    public async Task EmptyInvalidOrOversizeFilesAreRejectedBeforeCloud(int size)
    {
        var storage = new TestRecipeMediaStorage();
        using var app = await ItemTestApplication.Create(recipeMediaStorage: storage);
        using var response = await Upload(app, new byte[size]);
        Assert.Contains(response.StatusCode, new[] { HttpStatusCode.BadRequest, HttpStatusCode.RequestEntityTooLarge });
        Assert.Equal(0, storage.Calls);
    }

    [Fact]
    public async Task ConfiguredVideoLimitAndSeparateImageLimitAreEnforced()
    {
        var storage = new TestRecipeMediaStorage();
        using var app = await ItemTestApplication.Create(recipeMediaStorage: storage, maximumVideoBytes: 100);
        var video = new byte[101];
        "ftyp"u8.CopyTo(video.AsSpan(4));
        using var rejected = await Upload(app, video);
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        var image = new byte[2 * 1024 * 1024 + 1];
        Png().CopyTo(image, 0);
        using var imageRejected = await Upload(app, image);
        Assert.Equal(HttpStatusCode.BadRequest, imageRejected.StatusCode);
        Assert.Equal(0, storage.Calls);
    }

    [Theory]
    [InlineData(400)]
    [InlineData(503)]
    public async Task CloudFailureNeverCreatesRecipeData(int status)
    {
        using var app = await ItemTestApplication.Create(recipeMediaStorage: new TestRecipeMediaStorage
        {
            FailureStatus = status
        });
        using var response = await Upload(app, Png());
        Assert.Equal(status, (int)response.StatusCode);
        await using var db = app.OpenDatabase();
        Assert.Empty(await db.RecipeVersions.ToListAsync());
        Assert.Empty(await db.RecipeMedia.ToListAsync());
    }

    [Theory]
    [InlineData("image", "https://external.test/step.png")]
    [InlineData("image", "https://res.cloudinary.com/other/image/upload/catalog/recipes/images/11111111111111111111111111111111.png")]
    [InlineData("video", "javascript:alert(1)")]
    [InlineData("image", "https://res.cloudinary.com/test/image/upload/catalog/items/11111111111111111111111111111111.png")]
    [InlineData("document", "https://external.test/file.pdf")]
    public async Task NewExternalOrMismatchedMediaIsRejectedForBothRecipeOwners(string type, string url)
    {
        using var app = await ItemTestApplication.Create();
        var item = await OptionTestData.Item(app, "Rejected media");
        var option = await OptionTestData.Option(app, await OptionTestData.Group(app));
        using var first = await CreateRecipe(app, false, item, type, url);
        using var second = await CreateRecipe(app, true, option, type, url);
        Assert.Equal(HttpStatusCode.BadRequest, first.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);
        await using var db = app.OpenDatabase();
        Assert.Empty(await db.RecipeVersions.ToListAsync());
    }

    [Fact]
    public async Task LegacyMediaCanOnlyBeCarriedForwardByTheSameItem()
    {
        using var app = await ItemTestApplication.Create();
        var item = await OptionTestData.Item(app, "Legacy media");
        var other = await OptionTestData.Item(app, "Another recipe");
        var version = await app.AddRecipe(item, DateTime.UtcNow.AddDays(-1));
        const string oldUrl = "https://legacy.test/step.png";
        await using (var db = app.OpenDatabase())
        {
            db.RecipeMedia.Add(new RecipeMedium
            {
                RecipeMediaId = Guid.NewGuid(),
                RecipeVersionId = version,
                MediaType = "image",
                Url = oldUrl,
                DisplayOrder = 0
            });
            await db.SaveChangesAsync();
        }

        using var rejected = await CreateRecipe(app, false, other, "image", oldUrl);
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        using var allowed = await CreateRecipe(app, false, item, "image", oldUrl);
        Assert.Equal(HttpStatusCode.Created, allowed.StatusCode);
        await using var verification = app.OpenDatabase();
        Assert.Equal(2, await verification.RecipeMedia.CountAsync());
        Assert.All(await verification.RecipeMedia.ToListAsync(), media => Assert.Equal(oldUrl, media.Url));
    }

    [Theory]
    [InlineData("https://www.youtube.com/watch?v=recipe-demo&t=30s")]
    [InlineData("https://youtu.be/recipe-demo")]
    [InlineData("https://videos.example.test/step.mp4?token=test-only#start")]
    [InlineData("http://videos.example.test/step.webm")]
    public async Task VideoLinksCanBeSavedForItemsAndOptionsWithoutCloudUpload(string url)
    {
        var storage = new TestRecipeMediaStorage();
        using var app = await ItemTestApplication.Create(recipeMediaStorage: storage);
        var item = await OptionTestData.Item(app, "Video link");
        var option = await OptionTestData.Option(app, await OptionTestData.Group(app));
        using var itemResponse = await CreateRecipe(app, false, item, "video", url);
        using var optionResponse = await CreateRecipe(app, true, option, "video", url);
        Assert.Equal(HttpStatusCode.Created, itemResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Created, optionResponse.StatusCode);
        var itemResult = (await itemResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("result");
        var optionResult = (await optionResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("result");
        Assert.Equal(url, itemResult.GetProperty("media")[0].GetProperty("url").GetString());
        Assert.Equal(url, optionResult.GetProperty("media")[0].GetProperty("url").GetString());
        Assert.Equal(0, storage.Calls);
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:video/mp4;base64,AAAA")]
    [InlineData("file:///C:/video.mp4")]
    [InlineData("/video.mp4")]
    [InlineData("https://name:password@videos.example.test/video.mp4")]
    [InlineData("https://videos.example.test/video\r\n.mp4")]
    public async Task UnsafeVideoLinksCannotCreateEitherRecipeType(string url)
    {
        using var app = await ItemTestApplication.Create();
        var item = await OptionTestData.Item(app, "Unsafe video link");
        var option = await OptionTestData.Option(app, await OptionTestData.Group(app));
        using var first = await CreateRecipe(app, false, item, "video", url);
        using var second = await CreateRecipe(app, true, option, "video", url);
        Assert.Equal(HttpStatusCode.BadRequest, first.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);
        await using var db = app.OpenDatabase();
        Assert.Empty(await db.RecipeVersions.ToListAsync());
    }

    private static Task<HttpResponseMessage> CreateRecipe(ItemTestApplication app, bool option, Guid id, string type, string url)
    {
        return app.Client.PostAsJsonAsync($"/api/{(option ? "options" : "items")}/{id}/recipes", new
        {
            ingredients = new[]
            {
                new
                {
                    ingredientRef = Guid.NewGuid(),
                    unitRef = Guid.NewGuid(),
                    quantity = 1m
                }
            },
            media = new[]
            {
                new
                {
                    mediaType = type,
                    url,
                    displayOrder = 0
                }
            }
        });
    }

    [Fact]
    public async Task InvalidMediaDoesNotCloseTheExistingActiveRecipe()
    {
        using var app = await ItemTestApplication.Create();
        var item = await OptionTestData.Item(app, "Existing active recipe");
        var active = await app.AddRecipe(item, DateTime.UtcNow.AddDays(-1));
        using var response = await CreateRecipe(app, false, item, "image", "https://external.test/new.png");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await using var db = app.OpenDatabase();
        var saved = Assert.Single(await db.RecipeVersions.ToListAsync());
        Assert.Equal(active, saved.RecipeVersionId);
        Assert.Null(saved.EffectiveTo);
    }

    [Fact]
    public async Task NullMediaEntryReturns400InsteadOfServerError()
    {
        using var app = await ItemTestApplication.Create();
        var item = await OptionTestData.Item(app, "Null media");
        using var response = await app.Client.PostAsJsonAsync($"/api/items/{item}/recipes", new
        {
            media = new object?[] { null }
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static async Task<HttpResponseMessage> Upload(ItemTestApplication app, byte[] bytes)
    {
        using var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(bytes), "file", "untrusted-name.bin");
        return await app.Client.PostAsync("/api/recipes/media", form);
    }

    private static byte[] Png()
    {
        return Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+a8ioAAAAASUVORK5CYII=");
    }
}
