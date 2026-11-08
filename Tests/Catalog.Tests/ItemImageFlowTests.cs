using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Catalog.Tests;

public sealed class ItemImageFlowTests
{
    [Fact]
    public async Task UploadedUrlCanBeSavedOnItem()
    {
        using var app = await ItemTestApplication.Create();
        using var form = ImageForm();
        var response = await app.Client.PostAsync("/api/items/images", form);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var url = json.GetProperty("result").GetProperty("imageUrl").GetString();
        Assert.StartsWith("https://res.cloudinary.com/", url);
        var categoryId = await app.AddCategory();
        var created = await app.Client.PostAsJsonAsync("/api/items", new
        {
            categoryId,
            itemName = "Cloud image dish",
            imageUrl = url
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var item = await created.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(url, item.GetProperty("result").GetProperty("imageUrl").GetString());
    }

    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData("waiter", HttpStatusCode.Forbidden)]
    public async Task UploadRequiresMenuManager(string? token, HttpStatusCode expected)
    {
        var storage = new TestImageStorage();
        using var app = await ItemTestApplication.Create(imageStorage: storage);
        app.Client.DefaultRequestHeaders.Authorization = token is null ? null : new AuthenticationHeaderValue("Bearer", token);
        using var form = ImageForm();
        Assert.Equal(expected, (await app.Client.PostAsync("/api/items/images", form)).StatusCode);
        Assert.Equal(0, storage.UploadCount);
    }

    [Fact]
    public async Task UploadFailureReturns503()
    {
        using var app = await ItemTestApplication.Create(imageStorage: new TestImageStorage
        {
            FailUpload = true
        });
        using var form = ImageForm();
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await app.Client.PostAsync("/api/items/images", form)).StatusCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2 * 1024 * 1024 + 1)]
    public async Task InvalidFileSizeDoesNotUpload(int size)
    {
        var storage = new TestImageStorage();
        using var app = await ItemTestApplication.Create(imageStorage: storage);
        using var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(new byte[size]), "file", "image.png");
        var response = await app.Client.PostAsync("/api/items/images", form);
        Assert.Contains(response.StatusCode, new[] { HttpStatusCode.BadRequest, HttpStatusCode.RequestEntityTooLarge });
        Assert.Equal(0, storage.UploadCount);
    }

    private static MultipartFormDataContent ImageForm()
    {
        var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aD1sAAAAASUVORK5CYII=")), "file", "image.png");
        return form;
    }
}
