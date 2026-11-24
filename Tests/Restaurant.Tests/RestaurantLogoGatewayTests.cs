using System.Net;
using System.Net.Http.Json;
using ApiGateway.DTOs.Restaurant;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Restaurant.DAL.Models;
using Shared.Common.Images;
using Xunit;

namespace Restaurant.Tests;

public sealed partial class GatewayFlowTests
{
    private readonly LogoImageStorage _logoImages = new();

    [Fact]
    public async Task LogoUploadUpdatesOnlyTheRequestedRestaurantAndIsVisibleToAnonymousScan()
    {
        var selected = await CreateRestaurant();
        var other = await CreateRestaurant();
        var area = await CreateArea(selected.RestaurantId, "LOGO");
        var table = await CreateTable(area.AreaId);
        var qr = await Read<QrCreatedDto>(await _http.PostAsync($"/api/qr/tables/{table.TableId}", null), HttpStatusCode.Created);
        var uploaded = await Read<LogoResult>(await UploadLogo(selected.RestaurantId, LogoPng()));
        Assert.Equal(selected.RestaurantId, uploaded.RestaurantId);
        Assert.Equal(_logoImages.LastUrl, uploaded.LogoUrl);
        Assert.StartsWith($"restaurant/logos/{selected.RestaurantId:N}/", _logoImages.LastPublicId);
        using (var scope = _restaurant.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RestaurantDbContext>();
            var saved = await db.Restaurants.SingleAsync(item => item.RestaurantId == selected.RestaurantId);
            Assert.Equal(uploaded.LogoUrl, saved.LogoUrl);
            Assert.Equal(selected.RestaurantName, saved.RestaurantName);
            Assert.Equal(selected.Address, saved.Address);
            Assert.Equal(selected.Hotline, saved.Hotline);
            Assert.Null((await db.Restaurants.SingleAsync(item => item.RestaurantId == other.RestaurantId)).LogoUrl);
            Assert.Equal(2, await db.OpeningHours.CountAsync());
        }

        _http.DefaultRequestHeaders.Authorization = null;
        var scan = await Read<TableSessionScanDto>(await Scan(qr.Token));
        Assert.Equal(uploaded.LogoUrl, scan.LogoUrl);
        Assert.Equal(selected.RestaurantId, scan.RestaurantId);
    }

    [Theory]
    [InlineData(null, 401)]
    [InlineData("revoked", 401)]
    [InlineData("restaurant-admin", 403)]
    [InlineData("manager", 403)]
    [InlineData("waiter", 403)]
    [InlineData("customer", 403)]
    public async Task LogoUploadKeepsExistingPlatformAdministratorPermission(string? token, int expected)
    {
        var selected = await CreateRestaurant();
        _http.DefaultRequestHeaders.Authorization = token is null ? null : new("Bearer", token);
        using var response = await UploadLogo(selected.RestaurantId, LogoPng());
        Assert.Equal(expected, (int)response.StatusCode);
        Assert.Equal(0, _logoImages.Calls);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(32)]
    [InlineData(2097153)]
    public async Task InvalidLogoNeverReachesCloud(int size)
    {
        var selected = await CreateRestaurant();
        using var response = await UploadLogo(selected.RestaurantId, new byte[size]);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, _logoImages.Calls);
    }

    [Fact]
    public async Task MissingRestaurantDoesNotUploadAnOrphanLogo()
    {
        using var response = await UploadLogo(Guid.NewGuid(), LogoPng());
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(0, _logoImages.Calls);
    }

    [Fact]
    public async Task MissingLogoFileIsRejected()
    {
        var selected = await CreateRestaurant();
        using var form = new MultipartFormDataContent();
        using var response = await _http.PostAsync($"/api/restaurants/{selected.RestaurantId}/logo", form);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, _logoImages.Calls);
    }

    [Fact]
    public async Task UploadDoesNotOverwriteRestaurantDetailsChangedWhileCloudIsRunning()
    {
        var selected = await CreateRestaurant();
        _logoImages.BeforeReturn = async () =>
        {
            using var scope = _restaurant.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<RestaurantDbContext>();
            await db.Restaurants.Where(item => item.RestaurantId == selected.RestaurantId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.RestaurantName, "Changed during upload"));
        };
        await Read<LogoResult>(await UploadLogo(selected.RestaurantId, LogoPng()));
        var actual = await Read<RestaurantDto>(await _http.GetAsync($"/api/restaurants/{selected.RestaurantId}"));
        Assert.Equal("Changed during upload", actual.RestaurantName);
        Assert.Equal(_logoImages.LastUrl, actual.LogoUrl);
        Assert.Equal(selected.Hotline, actual.Hotline);
    }

    [Theory]
    [InlineData(400)]
    [InlineData(503)]
    public async Task FailedLogoUploadKeepsExistingImageAndRestaurantDetails(int status)
    {
        var selected = await CreateRestaurant();
        await SetLegacyLogo(selected.RestaurantId);
        _logoImages.FailureStatus = status;
        using var response = await UploadLogo(selected.RestaurantId, LogoPng());
        Assert.Equal(status, (int)response.StatusCode);
        var current = await Read<RestaurantDto>(await _http.GetAsync($"/api/restaurants/{selected.RestaurantId}"));
        Assert.Equal("https://legacy.test/logo.png", current.LogoUrl);
        Assert.Equal(selected.RestaurantName, current.RestaurantName);
        Assert.Equal(selected.OpeningHours.Count, current.OpeningHours.Count);
    }

    [Fact]
    public async Task RevocationDuringLogoUploadPreventsDatabaseChange()
    {
        var selected = await CreateRestaurant();
        await SetLegacyLogo(selected.RestaurantId);
        _logoImages.BeforeReturn = () =>
        {
            _logoImages.Revoked = true;
            return Task.CompletedTask;
        };
        using var response = await UploadLogo(selected.RestaurantId, LogoPng());
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(1, _logoImages.Calls);
        using var scope = _restaurant.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RestaurantDbContext>();
        Assert.Equal("https://legacy.test/logo.png", (await db.Restaurants.SingleAsync()).LogoUrl);
    }

    [Fact]
    public async Task RestaurantJsonRejectsNewLogoUrlsAndPreservesOrExplicitlyClearsExistingLogo()
    {
        var body = RestaurantBody();
        body.LogoUrl = "https://external.test/logo.png";
        using var rejectedCreate = await _http.PostAsJsonAsync("/api/restaurants", body);
        Assert.Equal(HttpStatusCode.BadRequest, rejectedCreate.StatusCode);
        var selected = await CreateRestaurant();
        await SetLegacyLogo(selected.RestaurantId);
        using var rejectedUpdate = await _http.PutAsJsonAsync($"/api/restaurants/{selected.RestaurantId}", body);
        Assert.Equal(HttpStatusCode.BadRequest, rejectedUpdate.StatusCode);
        body.LogoUrl = null;
        var preserved = await Read<RestaurantDto>(await _http.PutAsJsonAsync($"/api/restaurants/{selected.RestaurantId}", body));
        Assert.Equal("https://legacy.test/logo.png", preserved.LogoUrl);
        body.LogoUrl = preserved.LogoUrl;
        Assert.Equal(preserved.LogoUrl, (await Read<RestaurantDto>(await _http.PutAsJsonAsync(
            $"/api/restaurants/{selected.RestaurantId}", body))).LogoUrl);
        body.LogoUrl = "";
        var cleared = await Read<RestaurantDto>(await _http.PutAsJsonAsync($"/api/restaurants/{selected.RestaurantId}", body));
        Assert.Null(cleared.LogoUrl);
        Assert.Equal(0, _logoImages.Calls);
    }

    private async Task SetLegacyLogo(Guid id)
    {
        using var scope = _restaurant.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RestaurantDbContext>();
        var item = await db.Restaurants.SingleAsync(item => item.RestaurantId == id);
        item.LogoUrl = "https://legacy.test/logo.png";
        await db.SaveChangesAsync();
    }

    private async Task<HttpResponseMessage> UploadLogo(Guid id, byte[] image)
    {
        using var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(image), "file", "logo.png");
        return await _http.PostAsync($"/api/restaurants/{id}/logo", form);
    }

    private static byte[] LogoPng()
    {
        return Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+a8ioAAAAASUVORK5CYII=");
    }

    private sealed class LogoResult
    {
        public Guid RestaurantId { get; set; }
        public string LogoUrl { get; set; } = "";
    }

    private sealed class LogoImageStorage : IImageStorage
    {
        public int Calls { get; private set; }
        public string? LastPublicId { get; private set; }
        public string? LastUrl { get; private set; }
        public int? FailureStatus { get; set; }
        public bool Revoked { get; set; }
        public Func<Task>? BeforeReturn { get; set; }

        public async Task<string> UploadAsync(byte[] image, string publicId, CancellationToken ct)
        {
            Calls++;
            LastPublicId = publicId;
            if (BeforeReturn is not null)
            {
                await BeforeReturn();
            }

            if (FailureStatus.HasValue)
            {
                throw new ImageStorageException(FailureStatus.Value, "Cloudinary upload failed.");
            }

            LastUrl = $"https://res.cloudinary.com/test/image/upload/{publicId}.png";
            return LastUrl;
        }

        public Task<byte[]> ReadAsync(string url, CancellationToken ct)
        {
            throw new NotSupportedException();
        }
    }
}
