extern alias restaurantapi;

using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.Options;
using Restaurant.Common.Settings;
using RestaurantMarker = restaurantapi::Restaurant.API.ApiAssembly;
using System.Net.Http.Json;
using ApiGateway.DTOs.Restaurant;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Restaurant.DAL.Models;
using Xunit;

namespace Restaurant.Tests;

public sealed partial class GatewayFlowTests
{
    [Fact]
    public async Task GuestCreatesPaymentRequestAndAssignedWaiterAcknowledgesThroughGateway()
    {
        var session = await CreateGuestSession();
        var restaurantId = await RestaurantForPaymentRequest(session.TableSessionId);
        var waiter = Guid.Parse("11111111-1111-1111-1111-111111111111");
        using (var scope = _restaurant.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RestaurantDbContext>();
            db.RestaurantStaffs.Add(new RestaurantStaff
            {
                RestaurantId = restaurantId,
                UserIdRef = waiter,
                IsActive = true
            });
            await db.SaveChangesAsync();
        }

        var join = await _http.PostAsJsonAsync($"/api/table-sessions/{session.TableSessionId}/guest-access/join",
            new { phoneOrCode = "123" });
        Assert.Equal(HttpStatusCode.OK, join.StatusCode);
        var path = $"/api/table-sessions/{session.TableSessionId}/payment-requests";
        var id = Guid.NewGuid();
        _http.DefaultRequestHeaders.Add("Idempotency-Key", id.ToString());

        var created = await Read<PaymentRequestResult>(await _http.PostAsync(path, null));
        Assert.Equal(id, created.ServiceRequestId);
        Assert.Equal("Requested", created.Status);

        _http.DefaultRequestHeaders.Authorization = new("Bearer", "waiter");
        var queue = await Read<PaymentRequestPage>(
            await _http.GetAsync($"/api/restaurants/{restaurantId}/payment-requests"));
        var queued = Assert.Single(queue.Items);
        Assert.Equal(created.ServiceRequestId, queued.ServiceRequestId);
        Assert.Equal("T1", Assert.Single(queued.Tables).TableCode);
        var acknowledged = await Read<PaymentRequestResult>(await _http.PostAsync(path + $"/{id}/acknowledge", null));
        Assert.Equal(waiter, acknowledged.HandledByRef);
        Assert.Equal("Acknowledged", acknowledged.Status);

        _http.DefaultRequestHeaders.Authorization = null;
        var guest = await Read<PaymentRequestResult>(await _http.GetAsync(path + $"/{id}"));
        Assert.Equal(acknowledged.AcknowledgedAt, guest.AcknowledgedAt);
        Assert.Equal("Acknowledged", guest.Status);
        Assert.Null(guest.HandledByRef);
    }

    [Fact]
    public async Task PaymentRequestWaiterRoleLoadsWithoutBaseAppsettingsInDockerEnvironment()
    {
        using var host = new WebApplicationFactory<RestaurantMarker>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Docker");
            builder.UseSetting("ConnectionStrings:DefaultConnection", "Server=unused;Database=unused");
            builder.UseSetting("GrpcServices:Identity", "http://identity.test");
            builder.UseSetting("Reservations:ExpirationEnabled", "false");
            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                foreach (var source in configuration.Sources.OfType<JsonConfigurationSource>()
                    .Where(source => source.Path == "appsettings.json").ToArray())
                {
                    configuration.Sources.Remove(source);
                }
            });
        });
        var settings = host.Services.GetRequiredService<IOptions<ServiceRequestSettings>>().Value;
        Assert.Equal("waiter", Assert.Single(settings.WaiterRoleIds));
    }

    private async Task<Guid> RestaurantForPaymentRequest(Guid sessionId)
    {
        using var scope = _restaurant.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RestaurantDbContext>();
        return await db.SessionTables.Where(link => link.TableSessionId == sessionId)
            .Select(link => link.Table.Area.RestaurantId).SingleAsync();
    }
}

internal sealed record PaymentRequestResult(Guid ServiceRequestId, Guid TableSessionId, Guid RestaurantId,
    string Status, DateTimeOffset RequestedAt, DateTimeOffset? AcknowledgedAt, Guid? HandledByRef,
    PaymentRequestTableResult[] Tables);

internal sealed record PaymentRequestTableResult(Guid TableId, string TableCode);

internal sealed record PaymentRequestPage(PaymentRequestResult[] Items, int TotalCount);
