using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ApiGateway.DTOs.Restaurant;
using Restaurant.DAL.Models;
using Shared.Common.DTOs;
using Xunit;

namespace Restaurant.Tests;

public sealed partial class GatewayFlowTests
{
    [Fact]
    public async Task QrContainsTableAndRotationInvalidatesPreviousToken()
    {
        var restaurant = await CreateRestaurant();
        var area = await CreateArea(restaurant.RestaurantId, "QR");
        var table = await CreateTable(area.AreaId);
        var first = await Read<QrCreatedDto>(await _http.PostAsync($"/api/qr/tables/{table.TableId}", null), HttpStatusCode.Created);
        Assert.Equal("T1", first.Qr.TableCode);
        Assert.Contains("#qr=" + first.Token + "&table=T1", first.MenuUrl);
        Assert.Equal(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, first.Png.Take(8).ToArray());
        Assert.Equal(64, first.Token.Length);
        using (var scope = _restaurant.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RestaurantDbContext>();
            var stored = await db.Qrs.SingleAsync();
            Assert.NotEqual(first.Token, stored.TokenHash);
            Assert.DoesNotContain(first.Token, stored.TokenHash);
            Assert.Equal(first.Qr.ImageUrl, stored.ImageUrl);
        }

        Assert.Equal(HttpStatusCode.Conflict,
            (await _http.PostAsync($"/api/qr/tables/{table.TableId}", null)).StatusCode);
        var pageResponse = await _http.GetAsync($"/api/qr?areaId={area.AreaId}&search=T1&isActive=true");
        var pageJson = await pageResponse.Content.ReadAsStringAsync();
        Assert.DoesNotContain(first.Token, pageJson);
        Assert.DoesNotContain("tokenHash", pageJson, StringComparison.OrdinalIgnoreCase);
        var page = await pageResponse.Content.ReadFromJsonAsync<ResponseDTO<PageResult<QrDto>>>();
        Assert.Equal(first.Qr.QrId, Assert.Single(page!.Result!.Items).QrId);
        Assert.StartsWith("https://res.cloudinary.com/", page.Result.Items[0].ImageUrl);
        Assert.Equal(first.Qr.ImageUrl, page.Result.Items[0].ImageUrl);
        var imageResponse = await _http.GetAsync($"/api/qr/{first.Qr.QrId}/image");
        Assert.Equal(HttpStatusCode.OK, imageResponse.StatusCode);
        Assert.Equal("image/png", imageResponse.Content.Headers.ContentType!.MediaType);
        Assert.True(imageResponse.Headers.CacheControl!.NoStore);
        Assert.Equal(first.Png, await imageResponse.Content.ReadAsByteArrayAsync());
        Assert.Equal(first.Png, await _qrImages.ReadAsync(first.Qr.ImageUrl!, CancellationToken.None));

        _http.DefaultRequestHeaders.Authorization = null;
        var resolved = await Read<QrTableDto>(await _http.PostAsJsonAsync("/api/qr/resolve", new ResolveQrDto
        {
            Token = first.Token
        }));
        Assert.Equal(table.TableId, resolved.TableId);
        Assert.Equal(table.TableCode, resolved.TableCode);

        _http.DefaultRequestHeaders.Authorization = new("Bearer", "admin");
        var next = await Read<QrCreatedDto>(await _http.PostAsJsonAsync($"/api/qr/tables/{table.TableId}/regenerate",
            new RegenerateQrDto
            {
                ExpectedQrId = first.Qr.QrId
            }), HttpStatusCode.Created);
        Assert.NotEqual(first.Token, next.Token);
        Assert.Equal(next.Png, await _http.GetByteArrayAsync($"/api/qr/{next.Qr.QrId}/image"));
        Assert.Equal(first.Png, await _http.GetByteArrayAsync($"/api/qr/{first.Qr.QrId}/image"));
        Assert.Equal(HttpStatusCode.Conflict, (await _http.PostAsJsonAsync($"/api/qr/tables/{table.TableId}/regenerate",
            new RegenerateQrDto
            {
                ExpectedQrId = first.Qr.QrId
            })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _http.PostAsJsonAsync("/api/qr/resolve", new ResolveQrDto
        {
            Token = first.Token
        })).StatusCode);
        Assert.Equal(table.TableId, (await Read<QrTableDto>(await _http.PostAsJsonAsync("/api/qr/resolve", new ResolveQrDto
        {
            Token = next.Token
        }))).TableId);
        using var finalScope = _restaurant.Services.CreateScope();
        var finalDb = finalScope.ServiceProvider.GetRequiredService<RestaurantDbContext>();
        Assert.Equal(1, await finalDb.Qrs.CountAsync(x => x.RevokedAt == null));
        Assert.Empty(await finalDb.TableSessions.ToListAsync());
        Assert.Empty(await finalDb.GuestAccesses.ToListAsync());
    }

    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData("waiter", HttpStatusCode.Forbidden)]
    [InlineData("revoked", HttpStatusCode.Unauthorized)]
    public async Task QrManagementRequiresAdmin(string? token, HttpStatusCode expected)
    {
        _http.DefaultRequestHeaders.Authorization = token is null ? null : new("Bearer", token);
        var id = Guid.NewGuid();
        Assert.Equal(expected, (await _http.GetAsync("/api/qr")).StatusCode);
        Assert.Equal(expected, (await _http.GetAsync($"/api/qr/{id}/image")).StatusCode);
        Assert.Equal(expected, (await _http.PostAsync($"/api/qr/tables/{id}", null)).StatusCode);
        Assert.Equal(expected, (await _http.PostAsJsonAsync($"/api/qr/tables/{id}/regenerate",
            new RegenerateQrDto
            {
                ExpectedQrId = Guid.NewGuid()
            })).StatusCode);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task InactiveTableOrAreaRejectsIssueAndResolve(bool disableArea)
    {
        var restaurant = await CreateRestaurant();
        var area = await CreateArea(restaurant.RestaurantId, "QR");
        var table = await CreateTable(area.AreaId);
        var issued = await Read<QrCreatedDto>(await _http.PostAsync($"/api/qr/tables/{table.TableId}", null), HttpStatusCode.Created);
        var path = disableArea ? $"/api/areas/{area.AreaId}/status" : $"/api/tables/{table.TableId}/status";
        Assert.Equal(HttpStatusCode.OK, (await _http.PatchAsJsonAsync(path, new StatusDto
        {
            IsActive = false,
            Version = table.Version
        })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await _http.PostAsJsonAsync($"/api/qr/tables/{table.TableId}/regenerate",
            new RegenerateQrDto
            {
                ExpectedQrId = issued.Qr.QrId
            })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _http.PostAsJsonAsync("/api/qr/resolve", new ResolveQrDto
        {
            Token = issued.Token
        })).StatusCode);
    }

    [Fact]
    public async Task FailedRotationPreservesOldQr()
    {
        var restaurant = await CreateRestaurant();
        var area = await CreateArea(restaurant.RestaurantId, "QR");
        var table = await CreateTable(area.AreaId);
        var issued = await Read<QrCreatedDto>(await _http.PostAsync($"/api/qr/tables/{table.TableId}", null), HttpStatusCode.Created);
        using (var scope = _restaurant.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RestaurantDbContext>();
            await db.Database.ExecuteSqlRawAsync("""
                CREATE TRIGGER reject_qr BEFORE INSERT ON Qr
                BEGIN SELECT RAISE(ABORT, 'Simulated write failure'); END;
                """);
        }

        Assert.Equal(HttpStatusCode.BadGateway, (await _http.PostAsJsonAsync($"/api/qr/tables/{table.TableId}/regenerate",
            new RegenerateQrDto
            {
                ExpectedQrId = issued.Qr.QrId
            })).StatusCode);
        Assert.Equal(table.TableId, (await Read<QrTableDto>(await _http.PostAsJsonAsync("/api/qr/resolve", new ResolveQrDto
        {
            Token = issued.Token
        }))).TableId);
    }

    [Fact]
    public async Task MissingCloudImageReturns404WithoutRevokingQr()
    {
        var restaurant = await CreateRestaurant();
        var area = await CreateArea(restaurant.RestaurantId, "QR");
        var table = await CreateTable(area.AreaId);
        var issued = await Read<QrCreatedDto>(await _http.PostAsync($"/api/qr/tables/{table.TableId}", null), HttpStatusCode.Created);
        _qrImages.Images.Remove(issued.Qr.ImageUrl!);
        var page = await Read<PageResult<QrDto>>(await _http.GetAsync("/api/qr"));
        Assert.Equal(issued.Qr.ImageUrl, Assert.Single(page.Items).ImageUrl);
        Assert.Equal(HttpStatusCode.NotFound, (await _http.GetAsync($"/api/qr/{issued.Qr.QrId}/image")).StatusCode);
        Assert.Equal(table.TableId, (await Read<QrTableDto>(await _http.PostAsJsonAsync("/api/qr/resolve", new ResolveQrDto
        {
            Token = issued.Token
        }))).TableId);
    }

    [Fact]
    public async Task StorageFailureRollsBackRotationAndPreservesOldImage()
    {
        var restaurant = await CreateRestaurant();
        var area = await CreateArea(restaurant.RestaurantId, "QR");
        var table = await CreateTable(area.AreaId);
        var issued = await Read<QrCreatedDto>(await _http.PostAsync($"/api/qr/tables/{table.TableId}", null), HttpStatusCode.Created);
        _qrImages.FailUpload = true;
        try
        {
            Assert.Equal(HttpStatusCode.ServiceUnavailable, (await _http.PostAsJsonAsync($"/api/qr/tables/{table.TableId}/regenerate",
                new RegenerateQrDto
                {
                    ExpectedQrId = issued.Qr.QrId
                })).StatusCode);
        }
        finally
        {
            _qrImages.FailUpload = false;
        }

        Assert.Equal(issued.Png, await _http.GetByteArrayAsync($"/api/qr/{issued.Qr.QrId}/image"));
        Assert.Equal(table.TableId, (await Read<QrTableDto>(await _http.PostAsJsonAsync("/api/qr/resolve", new ResolveQrDto
        {
            Token = issued.Token
        }))).TableId);
        using var scope = _restaurant.Services.CreateScope();
        Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<RestaurantDbContext>().Qrs.CountAsync());
    }

    [Fact]
    public async Task UnknownTokensAndTablesAreRejected()
    {
        var orphanId = Guid.NewGuid();
        await _qrImages.SaveAsync(orphanId, [1, 2, 3], CancellationToken.None);
        Assert.Equal(HttpStatusCode.NotFound, (await _http.GetAsync($"/api/qr/{orphanId}/image")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _http.PostAsync($"/api/qr/tables/{Guid.NewGuid()}", null)).StatusCode);
        foreach (var token in new[] { "", "not-a-qr", new string('A', 64) })
        {
            Assert.Equal(HttpStatusCode.NotFound, (await _http.PostAsJsonAsync("/api/qr/resolve", new ResolveQrDto
            {
                Token = token
            })).StatusCode);
        }
    }
}
