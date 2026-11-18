extern alias billingapi;
extern alias restaurantapi;

using System.Net;
using System.Net.Http.Json;
using ApiGateway.DTOs.Restaurant;
using Backend.Contracts.Billing;
using Backend.Contracts.Restaurant;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Restaurant.DAL.Models;
using Shared.Common.Billing;
using Xunit;
using BillingMarker = billingapi::Billing.API.GrpcServices.PaymentSubmissionGrpcService;
using RestaurantMarker = restaurantapi::Restaurant.API.ApiAssembly;
using ClosureWorker = restaurantapi::Restaurant.API.Workers.PaidSessionClosureWorker;

namespace Restaurant.Tests;

public sealed partial class ReservationSqlServerTests
{
    private const string ClosureKey = "integration-test-internal-payment-closure-key";

    private async Task VerifyClosureRequiresPaidSession(WebApplicationFactory<RestaurantMarker> restaurant,
        WebApplicationFactory<BillingMarker> billing, LocationIds location, Guid sessionId, Guid otherRestaurant, HttpClient guest)
    {
        using var restaurantChannel = GrpcChannel.ForAddress("http://restaurant.test", new GrpcChannelOptions
        {
            HttpHandler = restaurant.Server.CreateHandler()
        });
        var closer = new PaidSessionClosureGrpc.PaidSessionClosureGrpcClient(restaurantChannel);
        var request = new PaidSessionClosureRequest
        {
            RestaurantId = location.RestaurantId.ToString(),
            TableSessionId = sessionId.ToString()
        };
        var unauthenticated = await Assert.ThrowsAsync<RpcException>(async () => await closer.CloseAfterPaymentAsync(request));
        Assert.Equal(StatusCode.Unauthenticated, unauthenticated.StatusCode);
        var wrongRestaurant = request.Clone();
        wrongRestaurant.RestaurantId = otherRestaurant.ToString();
        var rejected = await Assert.ThrowsAsync<RpcException>(async () => await closer.CloseAfterPaymentAsync(wrongRestaurant, Header()));
        Assert.Equal(StatusCode.PermissionDenied, rejected.StatusCode);
        Assert.False((await closer.CloseAfterPaymentAsync(request, Header())).Closed);

        using var billingChannel = GrpcChannel.ForAddress("http://billing.test", new GrpcChannelOptions
        {
            HttpHandler = billing.Server.CreateHandler()
        });
        var verifier = new PaymentSettlementGrpc.PaymentSettlementGrpcClient(billingChannel);
        var lookup = new VerifiedCheckoutRequest
        {
            RestaurantId = location.RestaurantId.ToString(),
            TableSessionId = sessionId.ToString()
        };
        var denied = await Assert.ThrowsAsync<RpcException>(async () => await verifier.GetVerifiedCheckoutAsync(lookup));
        Assert.Equal(StatusCode.Unauthenticated, denied.StatusCode);
        Assert.False((await verifier.GetVerifiedCheckoutAsync(lookup, Header())).HasCheckoutId);
        Assert.Null(await ReadClosedAt(sessionId));
        Assert.Equal(HttpStatusCode.OK, (await guest.GetAsync($"/api/table-sessions/{sessionId}/guest-access/me")).StatusCode);
    }

    private async Task VerifyClosureAfterRecovery(IServiceProvider restaurant, Guid sessionId, HttpClient guest)
    {
        Assert.Null(await ReadClosedAt(sessionId));
        Assert.Equal(HttpStatusCode.OK, (await guest.GetAsync($"/api/table-sessions/{sessionId}/guest-access/me")).StatusCode);
        using var worker = ActivatorUtilities.CreateInstance<ClosureWorker>(restaurant);
        using var wait = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await worker.StartAsync(wait.Token);
        try
        {
            while (!(await ReadClosedAt(sessionId)).HasValue)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(100), wait.Token);
            }
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }
    }

    private async Task VerifyClosedSession(WebApplicationFactory<RestaurantMarker> restaurant, LocationIds location,
        Guid sessionId, Guid requestId, HttpClient guest, HttpClient orderingGuest, HttpClient waiter)
    {
        var closedAt = await ReadClosedAt(sessionId);
        Assert.NotNull(closedAt);
        using var restaurantChannel = GrpcChannel.ForAddress("http://restaurant.test", new GrpcChannelOptions
        {
            HttpHandler = restaurant.Server.CreateHandler()
        });
        var closer = new PaidSessionClosureGrpc.PaidSessionClosureGrpcClient(restaurantChannel);
        Assert.True((await closer.CloseAfterPaymentAsync(new PaidSessionClosureRequest
        {
            RestaurantId = location.RestaurantId.ToString(),
            TableSessionId = sessionId.ToString()
        }, Header())).Closed);

        using (var scope = _restaurant.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RestaurantDbContext>();
            var session = await db.TableSessions.AsNoTracking().SingleAsync(value => value.TableSessionId == sessionId);
            Assert.Equal("Closed", session.Status);
            Assert.Equal(closedAt, session.ClosedAt);
            var request = await db.ServiceRequests.AsNoTracking().SingleAsync(value => value.ServiceRequestId == requestId);
            Assert.Equal("Completed", request.Status);
            Assert.Equal(closedAt, request.CompletedAt);
            var guests = await db.GuestAccesses.AsNoTracking().Where(value => value.TableSessionId == sessionId).ToListAsync();
            Assert.Equal(2, guests.Count);
            Assert.All(guests, value => Assert.Equal(closedAt, value.RevokedAt));
            var links = await db.SessionTables.AsNoTracking().Where(value => value.TableSessionId == sessionId).ToListAsync();
            Assert.Equal(closedAt, Assert.Single(links).ReleasedAt);
        }

        var accessPath = $"/api/table-sessions/{sessionId}/guest-access";
        Assert.Equal(HttpStatusCode.Unauthorized, (await guest.GetAsync(accessPath + "/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await guest.PostAsJsonAsync(accessPath + "/join", new { phoneOrCode = "123" })).StatusCode);
        using var order = new HttpRequestMessage(HttpMethod.Post, $"/api/table-sessions/{sessionId}/orders")
        {
            Content = JsonContent.Create(new
            {
                items = new[] { new { itemId = Guid.NewGuid(), quantity = 1 } }
            })
        };
        order.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        Assert.Equal(HttpStatusCode.Unauthorized, (await orderingGuest.SendAsync(order)).StatusCode);
        var next = await Read<TableSessionScanDto>(await waiter.PostAsync($"/api/table-sessions/tables/{location.TableId}/open", null));
        Assert.True(next.IsNewSession);
        Assert.NotEqual(sessionId, next.TableSessionId);
        Assert.Equal("Open", next.Status);
    }

    private async Task<DateTime?> ReadClosedAt(Guid sessionId)
    {
        using var scope = _restaurant.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RestaurantDbContext>();
        return await db.TableSessions.AsNoTracking().Where(value => value.TableSessionId == sessionId)
            .Select(value => value.ClosedAt).SingleAsync();
    }

    private static Metadata Header()
    {
        return new Metadata { { PaymentSettlementServiceAccess.Header, PaymentSettlementServiceAccess.CreateHeaderValue(ClosureKey) } };
    }

    private sealed class UnavailableClosureHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        }
    }
}
