extern alias billingapi;

using System.Net;
using System.Text.Json;
using Backend.Contracts.Billing;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Restaurant.DAL.Models;
using Xunit;
using BillingReport = billingapi::Billing.API.GrpcServices.RestaurantReportGrpcService;
using BillingRestaurant = billingapi::Backend.Contracts.Restaurant.RestaurantAccessGrpc.RestaurantAccessGrpcClient;

namespace Restaurant.Tests;

public sealed partial class GatewayFlowTests
{
    [Fact]
    public async Task ReportPeriodFlowsThroughGatewayBillingAndRestaurantWithoutFinancialTotals()
    {
        var restaurant = await CreateRestaurant();
        await AssignReportManager(restaurant.RestaurantId);
        await using var billing = ReportBillingHost();
        await using var gateway = ReportGatewayHost(billing);
        using var client = gateway.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", "manager");
        using var response = await client.GetAsync(ReportUrl(restaurant.RestaurantId) + "&timeZone=UTC");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl!.ToString());
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var result = json.RootElement.GetProperty("result");
        Assert.Equal(restaurant.RestaurantId.ToString(), result.GetProperty("restaurantId").GetString());
        Assert.Equal("Asia/Ho_Chi_Minh", result.GetProperty("timeZone").GetString());
        Assert.Equal("week", result.GetProperty("groupBy").GetString());
        Assert.Equal("Monday", result.GetProperty("weekStartsOn").GetString());
        Assert.Equal(DateTimeOffset.Parse("2026-11-24T17:00:00Z"), result.GetProperty("fromUtc").GetDateTimeOffset());
        Assert.True(result.GetProperty("startInclusive").GetBoolean());
        Assert.True(result.GetProperty("endExclusive").GetBoolean());
        Assert.False(result.TryGetProperty("totalAmount", out _));
        Assert.False(result.TryGetProperty("profit", out _));
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/api/restaurants/{restaurant.RestaurantId}")).StatusCode);
    }

    [Theory]
    [InlineData("", 401)]
    [InlineData("revoked", 401)]
    [InlineData("waiter", 403)]
    [InlineData("cashier", 403)]
    [InlineData("kitchen", 403)]
    [InlineData("customer", 403)]
    [InlineData("restaurant-admin", 403)]
    [InlineData("admin", 403)]
    public async Task ReportPeriodRejectsAnonymousAndOtherRoles(string role, int expected)
    {
        var restaurant = await CreateRestaurant();
        await AssignReportManager(restaurant.RestaurantId);
        await using var billing = ReportBillingHost();
        await using var gateway = ReportGatewayHost(billing);
        using var client = gateway.CreateClient();
        if (role.Length > 0)
        {
            client.DefaultRequestHeaders.Authorization = new("Bearer", role);
        }

        using var response = await client.GetAsync(ReportUrl(restaurant.RestaurantId));
        Assert.Equal(expected, (int)response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.False(json.RootElement.GetProperty("isSuccess").GetBoolean());
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("result").ValueKind);
    }

    [Fact]
    public async Task ReportPeriodRejectsAnotherRestaurantRevokedAssignmentAndHeaderMismatch()
    {
        var assigned = await CreateRestaurant();
        var other = await CreateRestaurant();
        await AssignReportManager(assigned.RestaurantId);
        await using var billing = ReportBillingHost();
        await using var gateway = ReportGatewayHost(billing);
        using var client = gateway.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", "manager");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(ReportUrl(other.RestaurantId))).StatusCode);
        client.DefaultRequestHeaders.Add("x-restaurant-id", other.RestaurantId.ToString());
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(ReportUrl(assigned.RestaurantId))).StatusCode);
        client.DefaultRequestHeaders.Remove("x-restaurant-id");
        using (var scope = _restaurant.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RestaurantDbContext>();
            var assignment = Assert.Single(db.RestaurantStaffs);
            assignment.IsActive = false;
            await db.SaveChangesAsync();
        }

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(ReportUrl(assigned.RestaurantId))).StatusCode);
    }

    [Fact]
    public async Task ReportPeriodRejectsInvalidBoundsThroughGatewayAndDirectGrpc()
    {
        var restaurant = await CreateRestaurant();
        await AssignReportManager(restaurant.RestaurantId);
        await using var billing = ReportBillingHost();
        await using var gateway = ReportGatewayHost(billing);
        using var client = gateway.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", "manager");
        var url = $"/api/restaurants/{restaurant.RestaurantId}/reports/period?from=2026-11-25&to=2026-11-26";
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync(url)).StatusCode);
        using var channel = GrpcChannel.ForAddress("http://localhost", new GrpcChannelOptions
        {
            HttpHandler = billing.Server.CreateHandler()
        });
        var grpc = new RestaurantReportGrpc.RestaurantReportGrpcClient(channel);
        var headers = new Metadata
        {
            { "authorization", "Bearer manager" }
        };
        var error = await Assert.ThrowsAsync<RpcException>(async () => await grpc.GetPeriodAsync(new ReportPeriodRequest
        {
            RestaurantId = restaurant.RestaurantId.ToString(),
            From = "2026-11-25T00:00:00Z",
            To = "2026-11-25T00:00:00Z",
            GroupBy = "day"
        }, headers));
        Assert.Equal(StatusCode.InvalidArgument, error.StatusCode);
    }

    [Fact]
    public async Task ReportPeriodUsesUpdatedServerZoneAndControlsInvalidConfiguration()
    {
        var restaurant = await CreateRestaurant();
        await AssignReportManager(restaurant.RestaurantId);
        using (var scope = _restaurant.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RestaurantDbContext>();
            var stored = Assert.Single(db.Restaurants);
            stored.TimeZone = "UTC";
            await db.SaveChangesAsync();
        }

        await using var billing = ReportBillingHost();
        await using var gateway = ReportGatewayHost(billing);
        using var client = gateway.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", "manager");
        using var response = await client.GetAsync(ReportUrl(restaurant.RestaurantId));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var result = json.RootElement.GetProperty("result");
        Assert.Equal("UTC", result.GetProperty("timeZone").GetString());
        Assert.Equal(TimeSpan.Zero, result.GetProperty("fromLocal").GetDateTimeOffset().Offset);
        using (var scope = _restaurant.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RestaurantDbContext>();
            var stored = Assert.Single(db.Restaurants);
            stored.TimeZone = "invalid/restaurant-time-zone";
            await db.SaveChangesAsync();
        }

        using var unavailable = await client.GetAsync(ReportUrl(restaurant.RestaurantId));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, unavailable.StatusCode);
        Assert.DoesNotContain("invalid/restaurant-time-zone", await unavailable.Content.ReadAsStringAsync());
    }

    private async Task AssignReportManager(Guid restaurantId)
    {
        using var scope = _restaurant.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RestaurantDbContext>();
        db.RestaurantStaffs.Add(new RestaurantStaff
        {
            RestaurantId = restaurantId,
            UserIdRef = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            IsActive = true
        });
        await db.SaveChangesAsync();
    }

    private WebApplicationFactory<BillingReport> ReportBillingHost()
    {
        return new WebApplicationFactory<BillingReport>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:DefaultConnection", "Server=localhost;Database=UnusedReportTest;Integrated Security=True;TrustServerCertificate=True");
            builder.UseSetting("GrpcServices:Restaurant", "http://restaurant.test");
            builder.UseSetting("GrpcServices:Ordering", "http://ordering.test");
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IHostedService>();
                services.AddGrpcClient<BillingRestaurant>()
                    .ConfigurePrimaryHttpMessageHandler(() => _restaurant.Server.CreateHandler());
            });
        });
    }

    private WebApplicationFactory<ApiGateway.ApiAssembly> ReportGatewayHost(WebApplicationFactory<BillingReport> billing)
    {
        return _gateway.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("GrpcServices:Billing", "http://billing.test");
            builder.ConfigureTestServices(services =>
            {
                services.AddGrpcClient<RestaurantReportGrpc.RestaurantReportGrpcClient>()
                    .ConfigurePrimaryHttpMessageHandler(() => billing.Server.CreateHandler());
            });
        });
    }

    private static string ReportUrl(Guid restaurantId)
    {
        return $"/api/restaurants/{restaurantId}/reports/period?from="
            + Uri.EscapeDataString("2026-11-25T00:00:00+07:00")
            + "&to=" + Uri.EscapeDataString("2026-11-26T00:00:00+07:00") + "&groupBy=week";
    }
}
