using System.Net;
using System.Text;
using Shared.Common.Images;
using Xunit;

namespace Foundation.Tests;

public sealed class CloudinaryImageStorageTests
{
    [Fact]
    public async Task UploadSendsCredentialsAndReturnsSecureUrl()
    {
        var handler = new StubHandler(async request =>
        {
            Assert.Equal("https://api.cloudinary.com/v1_1/test/image/upload", request.RequestUri!.AbsoluteUri);
            Assert.Equal("Basic", request.Headers.Authorization!.Scheme);
            Assert.Equal("key:secret", Encoding.UTF8.GetString(Convert.FromBase64String(request.Headers.Authorization.Parameter!)));
            var form = await request.Content!.ReadAsStringAsync();
            Assert.Contains("catalog/items/id", form);
            Assert.Contains("jpg,png,webp", form);
            return Json(HttpStatusCode.OK, "{\"secure_url\":\"https://res.cloudinary.com/test/image/upload/v1/catalog/items/id.png\"}");
        });
        using var client = new HttpClient(handler);
        var storage = new CloudinaryImageStorage(client, Settings());
        var url = await storage.UploadAsync([1, 2, 3], "catalog/items/id", CancellationToken.None);
        Assert.Equal("https://res.cloudinary.com/test/image/upload/v1/catalog/items/id.png", url);
    }

    [Theory]
    [InlineData(400, 400)]
    [InlineData(401, 503)]
    [InlineData(429, 503)]
    [InlineData(500, 503)]
    public async Task UploadErrorsDoNotExposeProviderResponse(int upstream, int expected)
    {
        using var client = new HttpClient(new StubHandler(_ => Task.FromResult(Json((HttpStatusCode)upstream, "secret"))));
        var storage = new CloudinaryImageStorage(client, Settings());
        var error = await Assert.ThrowsAsync<ImageStorageException>(() => storage.UploadAsync([1], "id", CancellationToken.None));
        Assert.Equal(expected, error.StatusCode);
        Assert.DoesNotContain("secret", error.Message);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("not json")]
    [InlineData("{\"secure_url\":\"http://localhost/image.png\"}")]
    [InlineData("{\"secure_url\":\"https://res.cloudinary.com/other/image/upload/a.png\"}")]
    public async Task InvalidUploadResponseIsRejected(string body)
    {
        using var client = new HttpClient(new StubHandler(_ => Task.FromResult(Json(HttpStatusCode.OK, body))));
        var error = await Assert.ThrowsAsync<ImageStorageException>(() =>
            new CloudinaryImageStorage(client, Settings()).UploadAsync([1], "id", CancellationToken.None));
        Assert.Equal(503, error.StatusCode);
    }

    [Fact]
    public async Task MissingCredentialsAndInvalidSizeDoNotSendRequests()
    {
        using var client = new HttpClient(new StubHandler(_ => throw new InvalidOperationException("Must not send.")));
        var storage = new CloudinaryImageStorage(client, new CloudinarySettings());
        Assert.Equal(503, (await Assert.ThrowsAsync<ImageStorageException>(() => storage.UploadAsync([1], "id", CancellationToken.None))).StatusCode);
        Assert.Equal(400, (await Assert.ThrowsAsync<ImageStorageException>(() => storage.UploadAsync([], "id", CancellationToken.None))).StatusCode);
        Assert.Equal(400, (await Assert.ThrowsAsync<ImageStorageException>(() => storage.UploadAsync(new byte[CloudinaryImageStorage.MaxImageBytes + 1], "id", CancellationToken.None))).StatusCode);
    }

    [Fact]
    public async Task ReadRejectsLocalUrlAndMapsMissingCloudImage()
    {
        using var client = new HttpClient(new StubHandler(request =>
        {
            Assert.Equal("res.cloudinary.com", request.RequestUri!.Host);
            Assert.Null(request.Headers.Authorization);
            return Task.FromResult(Json(HttpStatusCode.NotFound, "{}"));
        }));
        var storage = new CloudinaryImageStorage(client, Settings());
        Assert.Equal(404, (await Assert.ThrowsAsync<ImageStorageException>(() => storage.ReadAsync("http://localhost/a", CancellationToken.None))).StatusCode);
        Assert.Equal(404, (await Assert.ThrowsAsync<ImageStorageException>(() => storage.ReadAsync("https://res.cloudinary.com/test/image/upload/a.png", CancellationToken.None))).StatusCode);
    }

    [Fact]
    public async Task CallerCancellationIsPreserved()
    {
        using var source = new CancellationTokenSource();
        source.Cancel();
        using var client = new HttpClient(new StubHandler(_ => throw new OperationCanceledException(source.Token)));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new CloudinaryImageStorage(client, Settings()).UploadAsync([1], "id", source.Token));
    }

    private static CloudinarySettings Settings()
    {
        return new CloudinarySettings
        {
            CloudName = "test",
            ApiKey = "key",
            ApiSecret = "secret"
        };
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body)
    {
        return new HttpResponseMessage(status)
        {
            Content = new StringContent(body)
        };
    }

    private sealed class StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return send(request);
        }
    }
}
