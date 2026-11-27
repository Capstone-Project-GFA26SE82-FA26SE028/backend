extern alias restaurantapi;

using System.Net;
using System.Security.Cryptography;
using System.Text;
using Restaurant.Common.Exceptions;
using Shared.Common.Images;
using Xunit;
using Documents = restaurantapi::Restaurant.API.Management.CloudinaryRegistrationDocuments;

namespace Restaurant.Tests;

public sealed class RestaurantRegistrationDocumentTests
{
    [Fact]
    public async Task UploadUsesAuthenticatedDeliveryAndReturnsOnlyAssetId()
    {
        using var handler = new CloudinaryStub(async (request, ct) =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.EndsWith("/test-cloud/image/authenticated", request.RequestUri!.AbsolutePath);
            Assert.Equal("Basic", request.Headers.Authorization?.Scheme);
            var body = await request.Content!.ReadAsStringAsync(ct);
            Assert.Contains("public_id", body);
            Assert.Contains("allowed_formats", body);

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"asset_id":"test-asset","type":"authenticated","secure_url":"https://example.test/image"}""")
            };
        });
        using var http = new HttpClient(handler);
        var storage = new Documents(http, Settings(), TimeProvider.System);
        Assert.Equal("test-asset", await storage.UploadAsync([1, 2, 3], "test/document", default));
    }

    [Fact]
    public async Task UploadRejectsPublicDelivery()
    {
        using var handler = new CloudinaryStub((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"asset_id":"test-asset","type":"upload"}""")
        }));
        using var http = new HttpClient(handler);
        var storage = new Documents(http, Settings(), TimeProvider.System);
        var error = await Assert.ThrowsAsync<ManagementException>(() => storage.UploadAsync([1, 2, 3], "test/document", default));
        Assert.Equal(503, error.StatusCode);
    }

    [Fact]
    public async Task DownloadSignsAssetIdAndKeepsCredentialsOutOfUrl()
    {
        var time = new DateTimeOffset(2026, 11, 27, 14, 0, 0, TimeSpan.Zero);
        byte[] image = [1, 2, 3];
        using var handler = new CloudinaryStub(async (request, ct) =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.EndsWith("/test-cloud/asset/download", request.RequestUri!.AbsolutePath);
            Assert.Empty(request.RequestUri.Query);
            var body = await request.Content!.ReadAsStringAsync(ct);
            var signed = $"asset_id=test-asset&timestamp={time.ToUnixTimeSeconds()}test-secret";
            var signature = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(signed))).ToLowerInvariant();
            Assert.Contains("asset_id=test-asset", body);
            Assert.Contains($"signature={signature}", body);
            Assert.DoesNotContain("test-secret", body);

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(image)
            };
        });
        using var http = new HttpClient(handler);
        var storage = new Documents(http, Settings(), new FixedClock(time));
        Assert.Equal(image, await storage.ReadAsync("test-asset", default));
    }

    private static CloudinarySettings Settings()
    {
        return new CloudinarySettings
        {
            CloudName = "test-cloud",
            ApiKey = "test-key",
            ApiSecret = "test-secret"
        };
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow()
        {
            return now;
        }
    }

    private sealed class CloudinaryStub(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return send(request, cancellationToken);
        }
    }
}
