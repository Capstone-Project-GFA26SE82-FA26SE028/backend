using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Backend.Contracts.Restaurant;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Restaurant.DAL.Models;
using Restaurant.BLL.Interfaces;
using Restaurant.Common.Exceptions;
using Xunit;

namespace Restaurant.Tests;

public sealed partial class GatewayFlowTests
{
    private int _pendingPaymentOrders;

    [Fact]
    public async Task PaymentRequestCanBeAcknowledgedAndReviewedThroughGatewayWithoutChangingTheSession()
    {
        var (sessionId, restaurantId) = await SeedPaymentSession();
        var id = Guid.NewGuid();
        using var channel = ScopeChannel();
        var client = new ServiceRequestGrpc.ServiceRequestGrpcClient(channel);
        var request = new PaymentServiceRequest
        {
            TableSessionId = sessionId.ToString(),
            ServiceRequestId = id.ToString()
        };
        var created = await client.CreateAsync(request, ScopeHeaders("waiter"));
        Assert.Equal("Requested", created.Status);
        Assert.Equal(created, await client.CreateAsync(request, ScopeHeaders("waiter")));
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "waiter");
        var path = $"/api/table-sessions/{sessionId}/service-requests/{id}";
        Assert.Equal(HttpStatusCode.Conflict, (await _http.PostAsync(path + "/review", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _http.PostAsync(path + "/acknowledge", null)).StatusCode);
        _pendingPaymentOrders = 1;
        Assert.Equal(HttpStatusCode.Conflict, (await _http.PostAsync(path + "/review", null)).StatusCode);
        _pendingPaymentOrders = 0;
        var reviewed = await _http.PostAsync(path + "/review", null);
        Assert.Equal(HttpStatusCode.OK, reviewed.StatusCode);
        using var body = JsonDocument.Parse(await reviewed.Content.ReadAsStringAsync());
        Assert.Equal("Reviewed", body.RootElement.GetProperty("result").GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.OK, (await _http.PostAsync(path + "/review", null)).StatusCode);
        using var list = JsonDocument.Parse(await _http.GetStringAsync($"/api/service-requests?restaurantId={restaurantId}&status=Reviewed"));
        Assert.Equal(1, list.RootElement.GetProperty("result").GetProperty("totalCount").GetInt32());
        using var scope = _restaurant.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RestaurantDbContext>();
        var stored = await db.ServiceRequests.SingleAsync();
        Assert.NotNull(stored.HandledByRef);
        Assert.NotNull(stored.AcknowledgedAt);
        Assert.Null(stored.CompletedAt);
        Assert.Equal("Open", (await db.TableSessions.SingleAsync()).Status);
    }

    [Theory]
    [InlineData("cashier")]
    [InlineData("manager")]
    [InlineData("admin")]
    public async Task OnlyAnAssignedWaiterCanAcknowledgeAndReview(string role)
    {
        var (sessionId, _) = await SeedPaymentSession();
        var id = Guid.NewGuid();
        using var channel = ScopeChannel();
        var client = new ServiceRequestGrpc.ServiceRequestGrpcClient(channel);
        await client.CreateAsync(new PaymentServiceRequest
        {
            TableSessionId = sessionId.ToString(),
            ServiceRequestId = id.ToString()
        }, ScopeHeaders("waiter"));
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", role);
        var path = $"/api/table-sessions/{sessionId}/service-requests/{id}";
        Assert.Equal(HttpStatusCode.Forbidden, (await _http.PostAsync(path + "/acknowledge", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _http.PostAsync(path + "/review", null)).StatusCode);
    }

    [Fact]
    public async Task RevokedAssignmentOtherRestaurantAndClosedSessionCannotBeProcessed()
    {
        var (sessionId, restaurantId) = await SeedPaymentSession();
        var id = Guid.NewGuid();
        using var channel = ScopeChannel();
        var client = new ServiceRequestGrpc.ServiceRequestGrpcClient(channel);
        var request = new PaymentServiceRequest
        {
            TableSessionId = sessionId.ToString(),
            ServiceRequestId = id.ToString()
        };
        await client.CreateAsync(request, ScopeHeaders("waiter"));
        using (var scope = _restaurant.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RestaurantDbContext>();
            (await db.RestaurantStaffs.SingleAsync()).IsActive = false;
            await db.SaveChangesAsync();
        }

        var denied = await Assert.ThrowsAsync<RpcException>(async () => await client.AcknowledgeAsync(request, ScopeHeaders("waiter")));
        Assert.Equal(StatusCode.PermissionDenied, denied.StatusCode);
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "waiter");
        Assert.Equal(HttpStatusCode.Forbidden, (await _http.GetAsync($"/api/service-requests?restaurantId={restaurantId}")).StatusCode);
        using (var scope = _restaurant.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RestaurantDbContext>();
            (await db.RestaurantStaffs.SingleAsync()).IsActive = true;
            (await db.TableSessions.SingleAsync()).Status = "Closed";
            await db.SaveChangesAsync();
        }

        var closed = await Assert.ThrowsAsync<RpcException>(async () => await client.CreateAsync(request, ScopeHeaders("waiter")));
        Assert.Equal(StatusCode.FailedPrecondition, closed.StatusCode);
    }

    [Fact]
    public async Task DifferentPaymentRequestIdsCannotCreateDuplicateActiveRequests()
    {
        var (sessionId, _) = await SeedPaymentSession();
        using var channel = ScopeChannel();
        var client = new ServiceRequestGrpc.ServiceRequestGrpcClient(channel);
        await client.CreateAsync(new PaymentServiceRequest
        {
            TableSessionId = sessionId.ToString(),
            ServiceRequestId = Guid.NewGuid().ToString()
        }, ScopeHeaders("waiter"));
        var duplicate = await Assert.ThrowsAsync<RpcException>(async () => await client.CreateAsync(new PaymentServiceRequest
        {
            TableSessionId = sessionId.ToString(),
            ServiceRequestId = Guid.NewGuid().ToString()
        }, ScopeHeaders("waiter")));
        Assert.Equal(StatusCode.FailedPrecondition, duplicate.StatusCode);
    }

    [Fact]
    public async Task GuestMustHaveValidAccessToTheRequestedSessionAndCannotReviewIt()
    {
        var (sessionId, _) = await SeedPaymentSession();
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        using (var scope = _restaurant.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RestaurantDbContext>();
            db.GuestAccesses.Add(new GuestAccess
            {
                GuestAccessId = Guid.NewGuid(),
                TableSessionId = sessionId,
                TokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))),
                CreatedAt = DateTime.UtcNow,
                ExpiredAt = DateTime.UtcNow.AddHours(1)
            });
            await db.SaveChangesAsync();
        }

        using var channel = ScopeChannel();
        var client = new ServiceRequestGrpc.ServiceRequestGrpcClient(channel);
        var request = new PaymentServiceRequest
        {
            TableSessionId = sessionId.ToString(),
            ServiceRequestId = Guid.NewGuid().ToString(),
            GuestToken = new string('0', 64)
        };
        var denied = await Assert.ThrowsAsync<RpcException>(async () => await client.CreateAsync(request));
        Assert.Equal(StatusCode.Unauthenticated, denied.StatusCode);
        request.GuestToken = token;
        Assert.Equal("Requested", (await client.CreateAsync(request)).Status);
        var review = await Assert.ThrowsAsync<RpcException>(async () => await client.ReviewAsync(request));
        Assert.Equal(StatusCode.Unauthenticated, review.StatusCode);
    }

    [Fact]
    public async Task AnotherWaiterAndAnotherRestaurantCannotTakeOverARequest()
    {
        var (sessionId, restaurantId) = await SeedPaymentSession();
        var other = await CreateRestaurant();
        using var channel = ScopeChannel();
        var client = new ServiceRequestGrpc.ServiceRequestGrpcClient(channel);
        var request = new PaymentServiceRequest
        {
            TableSessionId = sessionId.ToString(),
            ServiceRequestId = Guid.NewGuid().ToString()
        };
        await client.CreateAsync(request, ScopeHeaders("waiter"));
        using (var scope = _restaurant.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RestaurantDbContext>();
            var stored = await db.ServiceRequests.SingleAsync();
            stored.Status = "Acknowledged";
            stored.HandledByRef = Guid.NewGuid();
            stored.AcknowledgedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }

        var conflict = await Assert.ThrowsAsync<RpcException>(async () => await client.ReviewAsync(request, ScopeHeaders("waiter")));
        Assert.Equal(StatusCode.FailedPrecondition, conflict.StatusCode);
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "waiter");
        Assert.Equal(HttpStatusCode.Forbidden, (await _http.GetAsync($"/api/service-requests?restaurantId={other.RestaurantId}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _http.GetAsync($"/api/service-requests?restaurantId={restaurantId}")).StatusCode);
    }

    private async Task<(Guid SessionId, Guid RestaurantId)> SeedPaymentSession()
    {
        var restaurant = await CreateRestaurant();
        var area = await CreateArea(restaurant.RestaurantId, "PAY");
        var table = await CreateTable(area.AreaId);
        var id = Guid.NewGuid();
        using var scope = _restaurant.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RestaurantDbContext>();
        db.RestaurantStaffs.Add(new RestaurantStaff
        {
            RestaurantId = restaurant.RestaurantId,
            UserIdRef = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            IsActive = true
        });
        db.TableSessions.Add(new TableSession
        {
            TableSessionId = id,
            Status = "Open",
            PartySize = 2,
            OpenedAt = DateTime.UtcNow,
            SessionTables =
            [
                new SessionTable
                {
                    SessionTableId = Guid.NewGuid(),
                    TableId = table.TableId,
                    AssignedAt = DateTime.UtcNow
                }
            ]
        });
        await db.SaveChangesAsync();
        return (id, restaurant.RestaurantId);
    }

    private sealed class PaymentRequestOrders(Func<int> pending) : IPaymentRequestOrderClient
    {
        public Task RequireFrozenAsync(Guid sessionId, Guid checkoutId, CancellationToken ct)
        {
            if (pending() != 0)
            {
                throw new ManagementException(409, "Pending orders must be confirmed.");
            }

            return Task.CompletedTask;
        }
    }
}
