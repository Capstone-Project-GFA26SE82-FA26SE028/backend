using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Restaurant.DAL.Models;
using Shared.Persistence.Notifications;
using Xunit;

namespace Restaurant.Tests;

public sealed partial class GatewayFlowTests
{
    [Fact]
    public async Task StaffCallCompletesIndependentlyOfPaymentAndPublishesOnlyOnce()
    {
        var (sessionId, restaurantId) = await PrepareStaffCall();
        var path = $"/api/table-sessions/{sessionId}/staff-calls";
        var id = Guid.NewGuid();
        _http.DefaultRequestHeaders.Add("Idempotency-Key", id.ToString());
        var created = await Read<StaffCallResult>(await _http.PostAsync(path, null));
        Assert.Equal("StaffCall", created.Type);
        Assert.Equal("Requested", created.Status);
        Assert.Null(created.CompletedAt);
        var retry = await Read<StaffCallResult>(await _http.PostAsync(path, null));
        Assert.Equal(created.ServiceRequestId, retry.ServiceRequestId);
        Assert.Equal(created.RequestedAt, retry.RequestedAt);
        Assert.Equal(id, (await Read<StaffCallResult>(await _http.GetAsync(path + "/current"))).ServiceRequestId);

        _http.DefaultRequestHeaders.Remove("Idempotency-Key");
        _http.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString());
        Assert.Equal(HttpStatusCode.Conflict, (await _http.PostAsync(path, null)).StatusCode);
        var payment = await Read<PaymentRequestResult>(await _http.PostAsync(
            $"/api/table-sessions/{sessionId}/payment-requests", null));
        Assert.Equal("Requested", payment.Status);
        Assert.Equal(HttpStatusCode.NotFound, (await _http.GetAsync(path + $"/{payment.ServiceRequestId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _http.GetAsync(
            $"/api/table-sessions/{sessionId}/payment-requests/{id}")).StatusCode);

        _http.DefaultRequestHeaders.Authorization = new("Bearer", "waiter");
        var queue = await Read<StaffCallPage>(await _http.GetAsync($"/api/restaurants/{restaurantId}/staff-calls"));
        Assert.Equal(id, Assert.Single(queue.Items).ServiceRequestId);
        Assert.Equal("T1", Assert.Single(queue.Items[0].Tables).TableCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _http.PostAsync(path + $"/{id}/complete", null)).StatusCode);
        var accepted = await Read<StaffCallResult>(await _http.PostAsync(path + $"/{id}/acknowledge", null));
        Assert.Equal("Acknowledged", accepted.Status);
        Assert.NotNull(accepted.AcknowledgedAt);
        Assert.NotNull(accepted.HandledByRef);
        Assert.Equal(accepted.AcknowledgedAt, (await Read<StaffCallResult>(
            await _http.PostAsync(path + $"/{id}/acknowledge", null))).AcknowledgedAt);
        var completed = await Read<StaffCallResult>(await _http.PostAsync(path + $"/{id}/complete", null));
        Assert.Equal("Completed", completed.Status);
        Assert.NotNull(completed.CompletedAt);
        Assert.Equal(completed.CompletedAt, (await Read<StaffCallResult>(
            await _http.PostAsync(path + $"/{id}/complete", null))).CompletedAt);
        Assert.Empty((await Read<StaffCallPage>(await _http.GetAsync($"/api/restaurants/{restaurantId}/staff-calls"))).Items);
        Assert.Equal("Completed", (await Read<StaffCallResult>(await _http.GetAsync(
            $"/api/restaurants/{restaurantId}/staff-calls/{id}"))).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await _http.PostAsync(
            path + $"/{payment.ServiceRequestId}/complete", null)).StatusCode);

        _http.DefaultRequestHeaders.Authorization = null;
        var guest = await Read<StaffCallResult>(await _http.GetAsync(path + $"/{id}"));
        Assert.Null(guest.HandledByRef);
        Assert.Equal("Completed", guest.Status);
        Assert.Equal(HttpStatusCode.NotFound, (await _http.GetAsync(path + "/current")).StatusCode);
        Assert.Equal("Requested", (await Read<PaymentRequestResult>(await _http.GetAsync(
            $"/api/table-sessions/{sessionId}/payment-requests/current"))).Status);

        using (var scope = _restaurant.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RestaurantDbContext>();
            Assert.Equal("Open", (await db.TableSessions.FindAsync(sessionId))!.Status);
            var events = await db.Set<NotificationOutbox>().ToListAsync();
            Assert.Equal(2, events.Count);
            Assert.Single(events, item => item.Payload.Contains("StaffCallRequested"));
            Assert.Single(events, item => item.Payload.Contains("PaymentRequestSubmitted"));
            Assert.All(events, item => Assert.Equal(restaurantId, item.RestaurantId));
        }

        _http.DefaultRequestHeaders.Remove("Idempotency-Key");
        _http.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString());
        Assert.Equal("Requested", (await Read<StaffCallResult>(await _http.PostAsync(path, null))).Status);
    }

    [Fact]
    public async Task StaffCallRejectsUnauthorizedRolesWrongRestaurantAndOtherHandler()
    {
        var (sessionId, restaurantId) = await PrepareStaffCall();
        var path = $"/api/table-sessions/{sessionId}/staff-calls";
        Assert.Equal(HttpStatusCode.BadRequest, (await _http.PostAsync(path, null)).StatusCode);
        var id = Guid.NewGuid();
        _http.DefaultRequestHeaders.Add("Idempotency-Key", id.ToString());
        using var noCookie = _gateway.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            HandleCookies = false
        });
        noCookie.DefaultRequestHeaders.Add("Idempotency-Key", id.ToString());
        Assert.Equal(HttpStatusCode.Unauthorized, (await noCookie.PostAsync(path, null)).StatusCode);
        await Read<StaffCallResult>(await _http.PostAsync(path, null));
        Assert.Equal(HttpStatusCode.Unauthorized, (await _http.PostAsync(path + $"/{id}/acknowledge", null)).StatusCode);
        foreach (var role in new[] { "admin", "manager", "cashier", "kitchen" })
        {
            _http.DefaultRequestHeaders.Authorization = new("Bearer", role);
            Assert.Equal(HttpStatusCode.Forbidden, (await _http.PostAsync(path + $"/{id}/acknowledge", null)).StatusCode);
        }

        _http.DefaultRequestHeaders.Authorization = new("Bearer", "waiter");
        Assert.Equal(HttpStatusCode.Forbidden, (await _http.GetAsync($"/api/restaurants/{Guid.NewGuid()}/staff-calls")).StatusCode);
        using (var scope = _restaurant.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RestaurantDbContext>();
            var request = await db.ServiceRequests.FindAsync(id);
            request!.Status = "Acknowledged";
            request.HandledByRef = Guid.NewGuid();
            request.AcknowledgedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }

        Assert.Equal(HttpStatusCode.Conflict, (await _http.PostAsync(path + $"/{id}/acknowledge", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _http.PostAsync(path + $"/{id}/complete", null)).StatusCode);
        using (var scope = _restaurant.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RestaurantDbContext>();
            var assignment = await db.RestaurantStaffs.SingleAsync(item => item.RestaurantId == restaurantId);
            assignment.IsActive = false;
            await db.SaveChangesAsync();
        }

        Assert.Equal(HttpStatusCode.Forbidden, (await _http.GetAsync($"/api/restaurants/{restaurantId}/staff-calls")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _http.PostAsync(path + $"/{id}/acknowledge", null)).StatusCode);
    }

    [Fact]
    public async Task PaymentClosureCancelsUnfinishedStaffCallsWithoutMarkingThemServed()
    {
        var (sessionId, restaurantId) = await PrepareStaffCall();
        var callId = Guid.NewGuid();
        _http.DefaultRequestHeaders.Add("Idempotency-Key", callId.ToString());
        await Read<StaffCallResult>(await _http.PostAsync($"/api/table-sessions/{sessionId}/staff-calls", null));
        _http.DefaultRequestHeaders.Remove("Idempotency-Key");
        var paymentId = Guid.NewGuid();
        _http.DefaultRequestHeaders.Add("Idempotency-Key", paymentId.ToString());
        var paymentPath = $"/api/table-sessions/{sessionId}/payment-requests";
        await Read<PaymentRequestResult>(await _http.PostAsync(paymentPath, null));
        _http.DefaultRequestHeaders.Authorization = new("Bearer", "waiter");
        await Read<PaymentRequestResult>(await _http.PostAsync(paymentPath + $"/{paymentId}/acknowledge", null));
        using (var scope = _restaurant.Services.CreateScope())
        {
            var service = ActivatorUtilities.CreateInstance<Restaurant.BLL.Implements.TableSessionService>(
                scope.ServiceProvider, new StaffCallVerifiedPayment(paymentId));
            Assert.True(await service.CloseAfterPayment(restaurantId, sessionId, default));
            var db = scope.ServiceProvider.GetRequiredService<RestaurantDbContext>();
            var call = await db.ServiceRequests.FindAsync(callId);
            var payment = await db.ServiceRequests.FindAsync(paymentId);
            Assert.Equal("Cancelled", call!.Status);
            Assert.NotNull(call.CompletedAt);
            Assert.Null(call.HandledByRef);
            Assert.Equal("Completed", payment!.Status);
        }

        Assert.Equal("Cancelled", (await Read<StaffCallResult>(await _http.GetAsync(
            $"/api/restaurants/{restaurantId}/staff-calls/{callId}"))).Status);
    }

    private sealed class StaffCallVerifiedPayment(Guid paymentId) : Restaurant.BLL.Interfaces.IVerifiedPaymentClient
    {
        public Task<Guid?> GetCheckoutAsync(Guid restaurantId, Guid sessionId, CancellationToken ct)
        {
            return Task.FromResult<Guid?>(paymentId);
        }
    }

    private async Task<(Guid SessionId, Guid RestaurantId)> PrepareStaffCall()
    {
        var session = await CreateGuestSession();
        var restaurantId = await RestaurantForPaymentRequest(session.TableSessionId);
        using (var scope = _restaurant.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RestaurantDbContext>();
            db.RestaurantStaffs.Add(new RestaurantStaff
            {
                RestaurantId = restaurantId,
                UserIdRef = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                IsActive = true
            });
            await db.SaveChangesAsync();
        }

        Assert.Equal(HttpStatusCode.OK, (await _http.PostAsJsonAsync(
            $"/api/table-sessions/{session.TableSessionId}/guest-access/join", new { phoneOrCode = "123" })).StatusCode);
        _http.DefaultRequestHeaders.Authorization = null;
        return (session.TableSessionId, restaurantId);
    }
}

internal sealed record StaffCallResult(Guid ServiceRequestId, Guid TableSessionId, Guid RestaurantId,
    string Type, string Status, DateTimeOffset RequestedAt, DateTimeOffset? AcknowledgedAt,
    DateTimeOffset? CompletedAt, Guid? HandledByRef, PaymentRequestTableResult[] Tables);

internal sealed record StaffCallPage(StaffCallResult[] Items, int TotalCount);
