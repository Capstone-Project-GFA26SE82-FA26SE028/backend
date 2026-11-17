using Backend.Contracts.Restaurant;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Restaurant.DAL.Models;
using Xunit;

namespace Restaurant.Tests;

public sealed partial class GatewayFlowTests
{
    [Fact]
    public async Task ProvisioningRequiresInternalKeyAndIsIdempotent()
    {
        var restaurant = await CreateRestaurant();
        using var channel = GrpcChannel.ForAddress("http://localhost", new GrpcChannelOptions
        {
            HttpHandler = _restaurant.Server.CreateHandler()
        });
        var client = new RestaurantStaffGrpc.RestaurantStaffGrpcClient(channel);
        var request = new RestaurantStaffRequest
        {
            RestaurantId = restaurant.RestaurantId.ToString(),
            UserIdRef = Guid.NewGuid().ToString()
        };
        var key = new string('s', 40);
        _restaurant.Services.GetRequiredService<IConfiguration>()["StaffProvisioning:ServiceKey"] = key;
        var error = await Assert.ThrowsAsync<RpcException>(async () => await client.ProvisionAsync(request));
        Assert.Equal(StatusCode.PermissionDenied, error.StatusCode);
        error = await Assert.ThrowsAsync<RpcException>(async () => await client.ProvisionAsync(request,
            new Metadata { { "x-staff-provisioning-key", "wrong" } }));
        Assert.Equal(StatusCode.PermissionDenied, error.StatusCode);
        var headers = new Metadata { { "x-staff-provisioning-key", key } };
        Assert.True((await client.ProvisionAsync(request, headers)).IsActive);
        Assert.True((await client.ProvisionAsync(request, headers)).IsActive);
        using var scope = _restaurant.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RestaurantDbContext>();
        Assert.Single(await db.RestaurantStaffs.Where(value => value.UserIdRef == Guid.Parse(request.UserIdRef)).ToListAsync());
        var other = await CreateRestaurant();
        request.RestaurantId = other.RestaurantId.ToString();
        error = await Assert.ThrowsAsync<RpcException>(async () => await client.ProvisionAsync(request, headers));
        Assert.Equal(StatusCode.FailedPrecondition, error.StatusCode);
    }
}
