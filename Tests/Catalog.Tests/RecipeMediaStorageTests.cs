using System.Net;
using Catalog.API.Clients;
using Shared.Common.Images;
using Xunit;

namespace Catalog.Tests;

public sealed class RecipeMediaStorageTests
{
    [Theory]
    [InlineData("image", "images", "png", "jpg,png,webp")]
    [InlineData("video", "videos", "mp4", "mp4,webm")]
    public async Task UploadUsesCorrectResourceEndpointAndAcceptsOnlyConfiguredCloudReply(
        string type, string folder, string extension, string formats)
    {
        var id = $"catalog/recipes/{folder}/{Guid.NewGuid():N}";
        var url = $"https://res.cloudinary.com/test/{type}/upload/v1700000000/{id}.{extension}";
        var handler = new Handler(async (request, ct) =>
        {
            Assert.Equal($"https://api.cloudinary.com/v1_1/test/{type}/upload", request.RequestUri!.ToString());
            Assert.Equal("Basic", request.Headers.Authorization!.Scheme);
            var body = await request.Content!.ReadAsStringAsync(ct);
            Assert.Contains(formats, body);
            Assert.Contains(id, body);
            return Reply(200, "{\"secure_url\":\"" + url + "\"}");
        });
        using var http = new HttpClient(handler);
        Assert.Equal(url, await Client(http).UploadAsync([1], type, id, default));
        Assert.Equal(1, handler.Calls);
    }

    [Theory]
    [InlineData(400, 400)]
    [InlineData(401, 503)]
    [InlineData(429, 503)]
    [InlineData(500, 503)]
    [InlineData(503, 503)]
    [InlineData(302, 503)]
    public async Task ProviderFailuresAreControlledWithoutRetryOrBodyDisclosure(int provider, int expected)
    {
        var handler = new Handler((_, _) => Task.FromResult(Reply(provider, "private provider detail")));
        using var http = new HttpClient(handler);
        var error = await Assert.ThrowsAsync<ImageStorageException>(() => Client(http).UploadAsync([1], "video", "id", default));
        Assert.Equal(expected, error.StatusCode);
        Assert.DoesNotContain("private", error.Message);
        Assert.Equal(1, handler.Calls);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("{\"secure_url\":\"https://external.test/video.mp4\"}")]
    [InlineData("{\"secure_url\":\"https://res.cloudinary.com/other/video/upload/catalog/recipes/videos/11111111111111111111111111111111.mp4\"}")]
    public async Task InvalidProviderRepliesAreRejected(string body)
    {
        using var http = new HttpClient(new Handler((_, _) => Task.FromResult(Reply(200, body))));
        var error = await Assert.ThrowsAsync<ImageStorageException>(() => Client(http).UploadAsync([1], "video", "id", default));
        Assert.Equal(503, error.StatusCode);
    }

    [Fact]
    public async Task MissingCredentialsNeverSendARequest()
    {
        var handler = new Handler((_, _) => throw new InvalidOperationException("Must not call provider."));
        using var http = new HttpClient(handler);
        var client = new CloudinaryRecipeMediaStorage(http, new CloudinarySettings());
        Assert.Equal(503, (await Assert.ThrowsAsync<ImageStorageException>(() =>
            client.UploadAsync([1], "image", "id", default))).StatusCode);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task ProviderTimeoutIsControlledAndCallerCancellationIsPreserved()
    {
        using var http = new HttpClient(new Handler(async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return Reply(200, "{}");
        }))
        {
            Timeout = TimeSpan.FromMilliseconds(100)
        };
        Assert.Equal(503, (await Assert.ThrowsAsync<ImageStorageException>(() =>
            Client(http).UploadAsync([1], "video", "id", default))).StatusCode);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Client(http).UploadAsync([1], "video", "id", cancellation.Token));
    }

    private static CloudinaryRecipeMediaStorage Client(HttpClient http)
    {
        return new CloudinaryRecipeMediaStorage(http, new CloudinarySettings
        {
            CloudName = "test",
            ApiKey = "test-only-key",
            ApiSecret = "test-only-secret"
        });
    }

    private static HttpResponseMessage Reply(int status, string body)
    {
        return new HttpResponseMessage((HttpStatusCode)status)
        {
            Content = new StringContent(body)
        };
    }

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            return send(request, ct);
        }
    }
}
