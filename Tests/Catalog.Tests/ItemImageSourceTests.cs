using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Catalog.Tests;

public sealed class ItemImageSourceTests
{
    private const string CloudImage = "https://res.cloudinary.com/test/image/upload/v1700000000/catalog/items/0123456789abcdef0123456789abcdef.png";

    [Theory]
    [InlineData("https://external.test/photo.png")]
    [InlineData("http://res.cloudinary.com/test/image/upload/catalog/items/0123456789abcdef0123456789abcdef.png")]
    [InlineData("https://res.cloudinary.com/other/image/upload/catalog/items/0123456789abcdef0123456789abcdef.png")]
    [InlineData("https://res.cloudinary.com/test/image/upload/identity/avatars/0123456789abcdef0123456789abcdef.png")]
    [InlineData("https://res.cloudinary.com/test/image/upload/restaurant/qr/0123456789abcdef0123456789abcdef.png")]
    [InlineData("https://res.cloudinary.com.evil.test/test/image/upload/catalog/items/0123456789abcdef0123456789abcdef.png")]
    [InlineData("https://name@res.cloudinary.com/test/image/upload/catalog/items/0123456789abcdef0123456789abcdef.png")]
    [InlineData("https://res.cloudinary.com:444/test/image/upload/catalog/items/0123456789abcdef0123456789abcdef.png")]
    [InlineData(CloudImage + "?other=image")]
    [InlineData(CloudImage + "#fragment")]
    [InlineData("https://res.cloudinary.com/test/image/upload/catalog/items/0123456789abcdef0123456789abcdef.svg")]
    public async Task NewImageFromOtherSourcesIsRejectedOnBothCreateAndUpdate(string url)
    {
        using var app = await ItemTestApplication.Create();
        var categoryId = await app.AddCategory();
        using var create = await app.Client.PostAsJsonAsync("/api/items", new
        {
            categoryId,
            itemName = "Rejected external image",
            imageUrl = url
        });
        Assert.Equal(HttpStatusCode.BadRequest, create.StatusCode);
        var existing = await app.CreateItem(categoryId);
        var id = existing.GetProperty("itemId").GetGuid();
        using var update = await app.Client.PutAsJsonAsync($"/api/items/{id}", new
        {
            categoryId,
            itemName = "Must not save",
            imageUrl = url,
            version = existing.GetProperty("version").GetString()
        });
        Assert.Equal(HttpStatusCode.BadRequest, update.StatusCode);
        await using var db = app.OpenDatabase();
        var saved = Assert.Single(await db.Items.ToListAsync());
        Assert.Equal(existing.GetProperty("imageUrl").GetString(), saved.ImageUrl);
        Assert.Equal(existing.GetProperty("itemName").GetString(), saved.ItemName);
    }

    [Fact]
    public async Task ConfiguredCloudUrlWithVersionCanBeSavedAndStillUsesOptimisticConcurrency()
    {
        using var app = await ItemTestApplication.Create();
        var categoryId = await app.AddCategory();
        var item = await app.CreateItem(categoryId);
        var id = item.GetProperty("itemId").GetGuid();
        var body = new
        {
            categoryId,
            itemName = "New cloud image",
            imageUrl = CloudImage,
            version = item.GetProperty("version").GetString()
        };
        using var updated = await app.Client.PutAsJsonAsync($"/api/items/{id}", body);
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        var result = await updated.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(CloudImage, result.GetProperty("result").GetProperty("imageUrl").GetString());
        using var repeated = await app.Client.PutAsJsonAsync($"/api/items/{id}", body);
        Assert.Equal(HttpStatusCode.Conflict, repeated.StatusCode);
    }

    [Fact]
    public async Task LegacyImageIsPreservedWhenOmittedOrUnchangedAndCanBeExplicitlyCleared()
    {
        using var app = await ItemTestApplication.Create();
        var categoryId = await app.AddCategory();
        var item = await app.CreateItem(categoryId);
        var id = item.GetProperty("itemId").GetGuid();
        const string legacy = "https://legacy.test/old.png";
        await using (var db = app.OpenDatabase())
        {
            var saved = await db.Items.SingleAsync();
            saved.ImageUrl = legacy;
            await db.SaveChangesAsync();
        }

        foreach (var next in new string?[] { null, legacy, "" })
        {
            var current = await app.Client.GetFromJsonAsync<JsonElement>($"/api/items/{id}");
            using var response = await app.Client.PutAsJsonAsync($"/api/items/{id}", new
            {
                categoryId,
                itemName = "Preserved legacy item",
                imageUrl = next,
                version = current.GetProperty("result").GetProperty("version").GetString()
            });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var result = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("result");
            if (next == "")
            {
                Assert.Equal(JsonValueKind.Null, result.GetProperty("imageUrl").ValueKind);
            }
            else
            {
                Assert.Equal(legacy, result.GetProperty("imageUrl").GetString());
            }
        }
    }

    [Fact]
    public async Task MissingCloudNameRejectsNewImageButDoesNotPreventCreatingItemsWithoutImages()
    {
        using var app = await ItemTestApplication.Create(cloudName: "");
        var categoryId = await app.AddCategory();
        using var rejected = await app.Client.PostAsJsonAsync("/api/items", new
        {
            categoryId,
            itemName = "Missing cloud",
            imageUrl = CloudImage
        });
        Assert.Equal(HttpStatusCode.ServiceUnavailable, rejected.StatusCode);
        using var allowed = await app.Client.PostAsJsonAsync("/api/items", new
        {
            categoryId,
            itemName = "No image needed"
        });
        Assert.Equal(HttpStatusCode.Created, allowed.StatusCode);
    }

    [Fact]
    public async Task RenamedNonImageIsRejectedBeforeCallingCloud()
    {
        var storage = new TestImageStorage();
        using var app = await ItemTestApplication.Create(imageStorage: storage);
        using var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(Encoding.UTF8.GetBytes("<svg>not a supported image</svg>")), "file", "photo.png");
        using var response = await app.Client.PostAsync("/api/items/images", form);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, storage.UploadCount);
    }

    [Fact]
    public async Task FileAtSizeLimitReachesCloudValidation()
    {
        var storage = new TestImageStorage();
        using var app = await ItemTestApplication.Create(imageStorage: storage);
        var bytes = new byte[2 * 1024 * 1024];
        new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }.CopyTo(bytes, 0);
        using var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(bytes), "file", "photo.png");
        using var response = await app.Client.PostAsync("/api/items/images", form);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, storage.UploadCount);
    }
}
