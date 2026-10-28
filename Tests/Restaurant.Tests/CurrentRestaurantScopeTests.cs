using System.Net;
using System.Net.Http.Headers;
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
    [Theory]
    [InlineData("manager")]
    [InlineData("cashier")]
    public async Task CurrentScopeReturnsOnlyCurrentUsersActiveAssignments(string role)
    {
        var assigned = await CreateRestaurant();
        var revoked = await CreateRestaurant();
        var otherEmployee = await CreateRestaurant();
        var userId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        using (var scope = _restaurant.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RestaurantDbContext>();
            db.RestaurantStaffs.AddRange(
                new RestaurantStaff
                {
                    RestaurantId = assigned.RestaurantId,
                    UserIdRef = userId,
                    IsActive = true
                },
                new RestaurantStaff
                {
                    RestaurantId = revoked.RestaurantId,
                    UserIdRef = userId,
                    IsActive = false
                },
                new RestaurantStaff
                {
                    RestaurantId = otherEmployee.RestaurantId,
                    UserIdRef = Guid.NewGuid(),
                    IsActive = true
                });
            await db.SaveChangesAsync();
        }

        using var channel = ScopeChannel();
        var client = new RestaurantAccessGrpc.RestaurantAccessGrpcClient(channel);
        var headers = ScopeHeaders(role);
        var reply = await client.GetCurrentScopeAsync(new CurrentRestaurantScopeRequest(), headers);
        Assert.Equal(userId.ToString(), reply.UserId);
        Assert.Equal(role, reply.RoleId);
        Assert.False(reply.IsAdministrator);
        Assert.Equal(assigned.RestaurantId.ToString(), Assert.Single(reply.RestaurantIds));
        await client.GetCurrentScopeAsync(new CurrentRestaurantScopeRequest
        {
            RestaurantId = assigned.RestaurantId.ToString()
        }, headers);
        var error = await Assert.ThrowsAsync<RpcException>(async () => await client.GetCurrentScopeAsync(
            new CurrentRestaurantScopeRequest
            {
                RestaurantId = revoked.RestaurantId.ToString()
            }, headers));
        Assert.Equal(StatusCode.PermissionDenied, error.StatusCode);

        using (var scope = _restaurant.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RestaurantDbContext>();
            var assignment = await db.RestaurantStaffs.SingleAsync(value => value.RestaurantId == assigned.RestaurantId);
            assignment.IsActive = false;
            await db.SaveChangesAsync();
        }

        reply = await client.GetCurrentScopeAsync(new CurrentRestaurantScopeRequest(), headers);
        Assert.Empty(reply.RestaurantIds);
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", role);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await _http.GetAsync($"/api/restaurants/{assigned.RestaurantId}/staff")).StatusCode);
    }

    [Fact]
    public async Task AdministratorScopeChecksTargetExistsWithoutRequiringAssignment()
    {
        var restaurant = await CreateRestaurant();
        using var channel = ScopeChannel();
        var client = new RestaurantAccessGrpc.RestaurantAccessGrpcClient(channel);
        var reply = await client.GetCurrentScopeAsync(new CurrentRestaurantScopeRequest
        {
            RestaurantId = restaurant.RestaurantId.ToString()
        }, ScopeHeaders("admin"));
        Assert.True(reply.IsAdministrator);
        Assert.Empty(reply.RestaurantIds);
        var error = await Assert.ThrowsAsync<RpcException>(async () => await client.GetCurrentScopeAsync(
            new CurrentRestaurantScopeRequest
            {
                RestaurantId = Guid.NewGuid().ToString()
            }, ScopeHeaders("admin")));
        Assert.Equal(StatusCode.NotFound, error.StatusCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("revoked")]
    public async Task CurrentScopeRequiresAuthenticatedCurrentUser(string role)
    {
        using var channel = ScopeChannel();
        var client = new RestaurantAccessGrpc.RestaurantAccessGrpcClient(channel);
        var error = await Assert.ThrowsAsync<RpcException>(async () => await client.GetCurrentScopeAsync(
            new CurrentRestaurantScopeRequest(), ScopeHeaders(role)));
        Assert.Equal(StatusCode.Unauthenticated, error.StatusCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("invalid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public async Task CurrentScopeRejectsInvalidTargetRestaurant(string restaurantId)
    {
        using var channel = ScopeChannel();
        var client = new RestaurantAccessGrpc.RestaurantAccessGrpcClient(channel);
        var error = await Assert.ThrowsAsync<RpcException>(async () => await client.GetCurrentScopeAsync(
            new CurrentRestaurantScopeRequest
            {
                RestaurantId = restaurantId
            }, ScopeHeaders("admin")));
        Assert.Equal(StatusCode.InvalidArgument, error.StatusCode);
    }

    private GrpcChannel ScopeChannel()
    {
        return GrpcChannel.ForAddress("http://localhost", new GrpcChannelOptions
        {
            HttpHandler = _restaurant.Server.CreateHandler()
        });
    }

    private static Metadata ScopeHeaders(string role)
    {
        var headers = new Metadata();
        if (!string.IsNullOrEmpty(role))
        {
            headers.Add("authorization", "Bearer " + role);
        }

        return headers;
    }
}
