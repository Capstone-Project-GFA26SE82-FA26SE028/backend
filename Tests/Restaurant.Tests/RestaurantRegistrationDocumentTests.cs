extern alias restaurantapi;

using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Restaurant.Common.Exceptions;
using Shared.Common.Images;
using Xunit;
using Documents = restaurantapi::Restaurant.API.Management.CloudinaryRegistrationDocuments;

namespace Restaurant.Tests;

public sealed class RestaurantRegistrationDocumentTests
{
    [Fact]
    public async Task UploadSignsAuthenticatedDeliveryAndReturnsOnlyAssetId()
    {
        var time = new DateTimeOffset(2026, 11, 27, 14, 0, 0, TimeSpan.Zero);
        using var handler = new CloudinaryStub(async (request, ct) =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.EndsWith("/test-cloud/image/upload", request.RequestUri!.AbsolutePath);
            Assert.Empty(request.RequestUri.Query);
            Assert.Null(request.Headers.Authorization);
            var form = Assert.IsType<MultipartFormDataContent>(request.Content);
            var fields = new Dictionary<string, string>();
            foreach (var part in form)
            {
                var name = part.Headers.ContentDisposition?.Name?.Trim('"');
                Assert.NotNull(name);
                if (name == "file")
                {
                    Assert.Equal(new byte[] { 1, 2, 3 }, await part.ReadAsByteArrayAsync(ct));
                    continue;
                }

                fields.Add(name, await part.ReadAsStringAsync(ct));
            }

            var signed = "allowed_formats=jpg,png,webp&overwrite=false&public_id=test/document"
                + $"&timestamp={time.ToUnixTimeSeconds()}&type=authenticatedtest-secret";
            var signature = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(signed))).ToLowerInvariant();
            Assert.Equal("test-key", fields["api_key"]);
            Assert.Equal("test/document", fields["public_id"]);
            Assert.Equal("authenticated", fields["type"]);
            Assert.Equal("false", fields["overwrite"]);
            Assert.Equal(signature, fields["signature"]);
            Assert.DoesNotContain("test-secret", fields.Values);

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"asset_id":"test-asset","type":"authenticated","secure_url":"https://example.test/image"}""")
            };
        });
        using var http = new HttpClient(handler);
        var storage = new Documents(http, Settings(), new FixedClock(time), NullLogger<Documents>.Instance);
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
        var storage = new Documents(http, Settings(), TimeProvider.System, NullLogger<Documents>.Instance);
        var error = await Assert.ThrowsAsync<ManagementException>(() => storage.UploadAsync([1, 2, 3], "test/document", default));
        Assert.Equal(503, error.StatusCode);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, 400)]
    [InlineData(HttpStatusCode.Unauthorized, 503)]
    [InlineData(HttpStatusCode.NotFound, 503)]
    [InlineData(HttpStatusCode.TooManyRequests, 503)]
    public async Task UploadProviderErrorsReturnControlledStatus(HttpStatusCode status, int expected)
    {
        using var handler = new CloudinaryStub((_, _) => Task.FromResult(new HttpResponseMessage(status)
        {
            Content = new StringContent("provider-only diagnostic")
        }));
        using var http = new HttpClient(handler);
        var storage = new Documents(http, Settings(), TimeProvider.System, NullLogger<Documents>.Instance);
        var error = await Assert.ThrowsAsync<ManagementException>(() => storage.UploadAsync([1, 2, 3], "test/document", default));
        Assert.Equal(expected, error.StatusCode);
        Assert.DoesNotContain("provider-only diagnostic", error.Message);
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
        var storage = new Documents(http, Settings(), new FixedClock(time), NullLogger<Documents>.Instance);
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
