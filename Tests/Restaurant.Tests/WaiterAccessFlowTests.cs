extern alias restaurantapi;

using System.Net;
using System.Net.Http.Headers;
using ApiGateway.DTOs.Restaurant;
using Backend.Contracts.Restaurant;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Restaurant.DAL.Models;
using Xunit;
using IdentityClient = restaurantapi::Backend.Contracts.Identity.UserGrpc.UserGrpcClient;
using IdentityRequest = restaurantapi::Backend.Contracts.Identity.GetMeRequest;
using IdentityReply = restaurantapi::Backend.Contracts.Identity.UserReply;
using IdentityUser = restaurantapi::Backend.Contracts.Identity.UserInfo;

namespace Restaurant.Tests;

public sealed partial class GatewayFlowTests
{
    [Theory]
    [InlineData("waiter", 200)]
    [InlineData("admin", 403)]
    [InlineData("manager", 403)]
    [InlineData("kitchen", 403)]
    [InlineData("cashier", 403)]
    [InlineData("customer", 403)]
    [InlineData("revoked", 401)]
    [InlineData("inactive", 401)]
    [InlineData("", 401)]
    public async Task ServiceTablesEnforceWaiterRoleThroughGateway(string role, int status)
    {
        _http.DefaultRequestHeaders.Authorization = string.IsNullOrEmpty(role)
            ? null : new AuthenticationHeaderValue("Bearer", role);
        var response = await _http.GetAsync("/api/tables/service");
        Assert.Equal(status, (int)response.StatusCode);
    }

    [Fact]
    public async Task WaiterSeesOpenSessionButCannotManageTables()
    {
        var restaurant = await CreateRestaurant();
        var area = await CreateArea(restaurant.RestaurantId, "WAITER");
        var table = await CreateTable(area.AreaId);
        var qr = await Read<QrCreatedDto>(await _http.PostAsync($"/api/qr/tables/{table.TableId}", null), HttpStatusCode.Created);
        var session = await Read<TableSessionScanDto>(await Scan(qr.Token));
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "waiter");
        var page = await Read<PageResult<ServiceTableDto>>(await _http.GetAsync($"/api/tables/service?areaId={area.AreaId}"));
        Assert.Equal(session.TableSessionId, Assert.Single(Assert.Single(page.Items).OpenSessionIds));
        Assert.Equal(HttpStatusCode.Forbidden, (await _http.GetAsync("/api/tables")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _http.DeleteAsync($"/api/tables/{table.TableId}?version={Uri.EscapeDataString(table.Version)}")).StatusCode);

        using (var scope = _restaurant.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RestaurantDbContext>();
            var link = await db.SessionTables.SingleAsync();
            link.ReleasedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }

        page = await Read<PageResult<ServiceTableDto>>(await _http.GetAsync($"/api/tables/service?areaId={area.AreaId}"));
        Assert.Empty(Assert.Single(page.Items).OpenSessionIds);
    }

    [Theory]
    [InlineData("admin")]
    [InlineData("manager")]
    [InlineData("kitchen")]
    public async Task DirectGrpcCannotBypassWaiterRole(string role)
    {
        using var channel = GrpcChannel.ForAddress("http://localhost", new GrpcChannelOptions
        {
            HttpHandler = _restaurant.Server.CreateHandler()
        });
        var client = new TableGrpc.TableGrpcClient(channel);
        var error = await Assert.ThrowsAsync<RpcException>(async () => await client.GetServiceTablesAsync(
            new ListRequest { Page = 1, PageSize = 20 }, new Metadata { { "authorization", "Bearer " + role } }));
        Assert.Equal(StatusCode.PermissionDenied, error.StatusCode);
        var sessions = new TableSessionGrpc.TableSessionGrpcClient(channel);
        var orderAccessError = await Assert.ThrowsAsync<RpcException>(async () => await sessions.GetForOrderingAsync(
            new OrderSessionRequest
            {
                TableSessionId = Guid.NewGuid().ToString()
            }, new Metadata { { "authorization", "Bearer " + role } }));
        Assert.Equal(StatusCode.PermissionDenied, orderAccessError.StatusCode);
    }

    private sealed class WaiterIdentityClient : IdentityClient
    {
        public override AsyncUnaryCall<IdentityReply> GetMeAsync(IdentityRequest request, Metadata? headers = null,
            DateTime? deadline = null, CancellationToken cancellationToken = default)
        {
            var role = headers?.GetValue("authorization")?.Replace("Bearer ", "") ?? "";
            var reply = new IdentityReply
            {
                IsSuccess = role != "revoked",
                StatusCode = role == "revoked" ? 401 : 200,
                Result = new IdentityUser
                {
                    UserId = "11111111-1111-1111-1111-111111111111",
                    RoleId = role,
                    IsActive = role != "inactive"
                }
            };
            return new AsyncUnaryCall<IdentityReply>(Task.FromResult(reply), Task.FromResult(new Metadata()),
                () => Status.DefaultSuccess, () => new Metadata(), () =>
                {
                });
        }
    }
}
