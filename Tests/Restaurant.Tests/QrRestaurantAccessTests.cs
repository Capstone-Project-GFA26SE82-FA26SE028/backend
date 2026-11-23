extern alias restaurantapi;

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ApiGateway.DTOs.Restaurant;
using Backend.Contracts.Restaurant;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Restaurant.DAL.Models;
using Xunit;

namespace Restaurant.Tests;

public sealed partial class GatewayFlowTests
{
    [Fact]
    public async Task RestaurantAdminManagesOwnQrAndCannotAccessForeignQrThroughGateway()
    {
        var own = await CreateRestaurant();
        var foreign = await CreateRestaurant();
        var ownArea = await CreateArea(own.RestaurantId, "OWN");
        var foreignArea = await CreateArea(foreign.RestaurantId, "OTHER");
        var ownTable = await CreateTable(ownArea.AreaId);
        var foreignTable = await CreateTable(foreignArea.AreaId);
        var foreignQr = await Read<QrCreatedDto>(await _http.PostAsync(
            $"/api/qr/tables/{foreignTable.TableId}", null), HttpStatusCode.Created);
        await AssignQrAdministrator(own.RestaurantId);
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "restaurant-admin");

        var issued = await Read<QrCreatedDto>(await _http.PostAsync(
            $"/api/qr/tables/{ownTable.TableId}", null), HttpStatusCode.Created);
        var page = await Read<PageResult<QrDto>>(await _http.GetAsync("/api/qr?pageSize=1"));
        Assert.Equal(1, page.TotalCount);
        Assert.Equal(issued.Qr.QrId, Assert.Single(page.Items).QrId);
        var image = await _http.GetAsync($"/api/qr/{issued.Qr.QrId}/image");
        Assert.Equal(HttpStatusCode.OK, image.StatusCode);
        Assert.Equal("image/png", image.Content.Headers.ContentType!.MediaType);
        Assert.NotEmpty(await image.Content.ReadAsByteArrayAsync());

        var rotated = await Read<QrCreatedDto>(await _http.PostAsJsonAsync(
            $"/api/qr/tables/{ownTable.TableId}/regenerate", new RegenerateQrDto
            {
                ExpectedQrId = issued.Qr.QrId
            }), HttpStatusCode.Created);
        Assert.NotEqual(issued.Qr.QrId, rotated.Qr.QrId);
        Assert.Equal(HttpStatusCode.NotFound, (await _http.PostAsJsonAsync("/api/qr/resolve", new ResolveQrDto
        {
            Token = issued.Token
        })).StatusCode);
        Assert.Equal(ownTable.TableId, (await Read<QrTableDto>(await _http.PostAsJsonAsync(
            "/api/qr/resolve", new ResolveQrDto
            {
                Token = rotated.Token
            }))).TableId);

        Assert.Equal(HttpStatusCode.Forbidden, (await _http.GetAsync(
            $"/api/qr?restaurantId={foreign.RestaurantId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _http.GetAsync(
            $"/api/qr/{foreignQr.Qr.QrId}/image")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _http.PostAsync(
            $"/api/qr/tables/{foreignTable.TableId}", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _http.PostAsJsonAsync(
            $"/api/qr/tables/{foreignTable.TableId}/regenerate", new RegenerateQrDto
            {
                ExpectedQrId = foreignQr.Qr.QrId
            })).StatusCode);
        var filtered = await Read<PageResult<QrDto>>(await _http.GetAsync($"/api/qr?areaId={foreignArea.AreaId}"));
        Assert.Empty(filtered.Items);
        Assert.Equal(0, filtered.TotalCount);

        using var channel = GrpcChannel.ForAddress("http://localhost", new GrpcChannelOptions
        {
            HttpHandler = _restaurant.Server.CreateHandler()
        });
        var client = new QrGrpc.QrGrpcClient(channel);
        var directError = await Assert.ThrowsAsync<RpcException>(async () => await client.GetImageAsync(new IdRequest
        {
            Id = foreignQr.Qr.QrId.ToString()
        }, new Metadata { { "authorization", "Bearer restaurant-admin" } }));
        Assert.Equal(StatusCode.PermissionDenied, directError.StatusCode);

        using var scope = _restaurant.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RestaurantDbContext>();
        var original = await db.Qrs.SingleAsync(qr => qr.QrId == foreignQr.Qr.QrId);
        Assert.Null(original.RevokedAt);
        Assert.Equal(3, await db.Qrs.CountAsync());
        Assert.Equal(3, _qrImages.Images.Count);
    }

    [Fact]
    public async Task RevokedQrAdministratorCannotReadOrIssueQrEvenThroughDirectGrpc()
    {
        var own = await CreateRestaurant();
        var area = await CreateArea(own.RestaurantId, "REVOKED");
        var table = await CreateTable(area.AreaId);
        var qr = await Read<QrCreatedDto>(await _http.PostAsync($"/api/qr/tables/{table.TableId}", null), HttpStatusCode.Created);
        await AssignQrAdministrator(own.RestaurantId, active: false);
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "restaurant-admin");
        Assert.Equal(HttpStatusCode.Forbidden, (await _http.GetAsync("/api/qr")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _http.GetAsync($"/api/qr/{qr.Qr.QrId}/image")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _http.PostAsync($"/api/qr/tables/{table.TableId}", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _http.PostAsJsonAsync(
            $"/api/qr/tables/{table.TableId}/regenerate", new RegenerateQrDto
            {
                ExpectedQrId = qr.Qr.QrId
            })).StatusCode);

        using var channel = GrpcChannel.ForAddress("http://localhost", new GrpcChannelOptions
        {
            HttpHandler = _restaurant.Server.CreateHandler()
        });
        var client = new QrGrpc.QrGrpcClient(channel);
        var error = await Assert.ThrowsAsync<RpcException>(async () => await client.CreateAsync(new IdRequest
        {
            Id = table.TableId.ToString()
        }, new Metadata { { "authorization", "Bearer restaurant-admin" } }));
        Assert.Equal(StatusCode.PermissionDenied, error.StatusCode);
        Assert.Single(_qrImages.Images);
    }

    [Theory]
    [InlineData("manager")]
    [InlineData("cashier")]
    [InlineData("kitchen")]
    [InlineData("inventory")]
    [InlineData("customer")]
    public async Task OtherAssignedRolesDoNotGainQrManagementRights(string role)
    {
        var own = await CreateRestaurant();
        await AssignQrAdministrator(own.RestaurantId);
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", role);
        var id = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.Forbidden, (await _http.GetAsync("/api/qr")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _http.GetAsync($"/api/qr/{id}/image")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _http.PostAsync($"/api/qr/tables/{id}", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _http.PostAsJsonAsync(
            $"/api/qr/tables/{id}/regenerate", new RegenerateQrDto
            {
                ExpectedQrId = Guid.NewGuid()
            })).StatusCode);
    }

    [Fact]
    public async Task AdministratorWithMultipleAssignmentsMustSelectRestaurantForQrList()
    {
        var first = await CreateRestaurant();
        var second = await CreateRestaurant();
        await AssignQrAdministrator(first.RestaurantId);
        await AssignQrAdministrator(second.RestaurantId);
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "restaurant-admin");

        Assert.Equal(HttpStatusCode.BadRequest, (await _http.GetAsync("/api/qr")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _http.GetAsync($"/api/qr?restaurantId={first.RestaurantId}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _http.GetAsync($"/api/qr?restaurantId={second.RestaurantId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _http.GetAsync($"/api/qr?restaurantId={Guid.NewGuid()}")).StatusCode);
    }

    private async Task AssignQrAdministrator(Guid restaurantId, bool active = true)
    {
        using var scope = _restaurant.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RestaurantDbContext>();
        db.RestaurantStaffs.Add(new RestaurantStaff
        {
            RestaurantId = restaurantId,
            UserIdRef = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            IsActive = active
        });
        await db.SaveChangesAsync();
    }
}
