using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Backend.Contracts.Restaurant;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Restaurant.BLL.Interfaces;
using Restaurant.DAL.Models;
using Shared.Common.Billing;
using Xunit;
using ApiGateway.DTOs.Restaurant;

namespace Restaurant.Tests;

public sealed partial class GatewayFlowTests
{
    private const string SettlementTestKey = "AQIDBAUGBwgJCgsMDQ4PEBESExQVFhcYGRobHB0eHyA=";
    private readonly SessionPaymentReader _sessionPaymentReader = new();

    [Fact]
    public async Task SignedPaidReceiptClosesSessionRevokesGuestsAndNextCustomerNeedsNoCleaningAction()
    {
        var payment = await ReviewedSettlement();
        Guid tableId;
        using (var scope = _restaurant.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RestaurantDbContext>();
            tableId = (await db.SessionTables.SingleAsync()).TableId;
            db.GuestAccesses.Add(new GuestAccess
            {
                GuestAccessId = Guid.NewGuid(),
                TableSessionId = payment.TableSessionId,
                TokenHash = new string('a', 64),
                CreatedAt = DateTime.UtcNow.AddMinutes(-5),
                ExpiredAt = DateTime.UtcNow.AddHours(1)
            });
            await db.SaveChangesAsync();
        }

        using var channel = ScopeChannel();
        var client = new SessionSettlementGrpc.SessionSettlementGrpcClient(channel);
        var first = await client.ClosePaidSessionAsync(Signed(payment));
        Assert.Equal(first.ClosedAt, (await client.ClosePaidSessionAsync(Signed(payment))).ClosedAt);
        Assert.True(PaymentSettlementSignature.Verify(PaymentSettlementSignature.ClosedPurpose,
            PaymentSettlementSignature.Closed(payment.TableSessionId, payment.RestaurantId, DateTimeOffset.Parse(first.ClosedAt, CultureInfo.InvariantCulture)),
            first.SignatureTime, first.Signature, SettlementTestKey, SettlementNow()));
        using (var scope = _restaurant.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RestaurantDbContext>();
            var session = await db.TableSessions.SingleAsync();
            Assert.Equal("Closed", session.Status);
            Assert.Equal(session.ClosedAt, (await db.SessionTables.SingleAsync()).ReleasedAt);
            Assert.Equal(session.ClosedAt, (await db.GuestAccesses.SingleAsync()).RevokedAt);
            Assert.Null((await db.SessionTables.SingleAsync()).ChangedByRef);
            Assert.Equal("Clean", (await db.Tables.SingleAsync()).CleaningStatus);
        }

        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "waiter");
        Assert.Equal(HttpStatusCode.OK, (await _http.GetAsync($"/api/table-sessions/{payment.TableSessionId}/service-requests/{payment.CheckoutId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _http.PostAsync($"/api/table-sessions/{payment.TableSessionId}/depart", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _http.PostAsJsonAsync($"/api/tables/{tableId}/confirm-clean", new { version = "unused" })).StatusCode);
        var next = await Read<TableSessionScanDto>(await _http.PostAsync($"/api/table-sessions/tables/{tableId}/open", null));
        Assert.NotEqual(payment.TableSessionId, next.TableSessionId);
        await client.ClosePaidSessionAsync(Signed(payment));
        using var verify = _restaurant.Services.CreateScope();
        var context = verify.ServiceProvider.GetRequiredService<RestaurantDbContext>();
        Assert.Null((await context.TableSessions.SingleAsync(session => session.TableSessionId == next.TableSessionId)).ClosedAt);
        Assert.Null((await context.SessionTables.SingleAsync(link => link.TableSessionId == next.TableSessionId)).ReleasedAt);
    }

    [Theory]
    [InlineData("unsigned")]
    [InlineData("tampered-bill")]
    [InlineData("expired")]
    public async Task EmployeeBearerAndForgedOrExpiredProofCannotCloseSession(string scenario)
    {
        var payment = await ReviewedSettlement();
        using var channel = ScopeChannel();
        var client = new SessionSettlementGrpc.SessionSettlementGrpcClient(channel);
        var request = Signed(payment);
        if (scenario == "unsigned")
        {
            request.Signature = string.Empty;
        }
        else if (scenario == "tampered-bill")
        {
            request.BillId = Guid.NewGuid().ToString();
        }
        else
        {
            request.SignatureTime -= 120;
            request.Signature = PaymentSettlementSignature.Sign(PaymentSettlementSignature.ProofPurpose,
                PaymentSettlementSignature.Proof(payment), request.SignatureTime, SettlementTestKey);
        }

        var error = await Assert.ThrowsAsync<RpcException>(async () => await client.ClosePaidSessionAsync(request, ScopeHeaders("cashier")));
        Assert.Equal(StatusCode.Unauthenticated, error.StatusCode);
        using var scope = _restaurant.Services.CreateScope();
        Assert.Null((await scope.ServiceProvider.GetRequiredService<RestaurantDbContext>().TableSessions.SingleAsync()).ClosedAt);
    }

    [Fact]
    public async Task EvenSignedProofMustMatchRestaurantAndReviewedCheckout()
    {
        var payment = await ReviewedSettlement();
        using var channel = ScopeChannel();
        var client = new SessionSettlementGrpc.SessionSettlementGrpcClient(channel);
        foreach (var value in new[] { payment with { RestaurantId = Guid.NewGuid() }, payment with { CheckoutId = Guid.NewGuid() } })
        {
            var error = await Assert.ThrowsAsync<RpcException>(async () => await client.ClosePaidSessionAsync(Signed(value)));
            Assert.Equal(StatusCode.FailedPrecondition, error.StatusCode);
        }

        using var scope = _restaurant.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RestaurantDbContext>();
        (await db.ServiceRequests.SingleAsync()).Status = "Acknowledged";
        await db.SaveChangesAsync();
        var unreviewed = await Assert.ThrowsAsync<RpcException>(async () => await client.ClosePaidSessionAsync(Signed(payment)));
        Assert.Equal(StatusCode.FailedPrecondition, unreviewed.StatusCode);
    }

    [Fact]
    public async Task RestartedScopeCanFindOpenReviewedSessionAndCloseOnlyAfterVerifiedPaymentExists()
    {
        var payment = await ReviewedSettlement();
        using (var scope = _restaurant.Services.CreateScope())
        {
            Assert.False(await scope.ServiceProvider.GetRequiredService<ITableSessionService>().TryClosePaidSession(payment.TableSessionId, default));
        }

        _sessionPaymentReader.Payment = payment;
        using (var restarted = _restaurant.Services.CreateScope())
        {
            Assert.True(await restarted.ServiceProvider.GetRequiredService<ITableSessionService>().TryClosePaidSession(payment.TableSessionId, default));
        }

        using var verify = _restaurant.Services.CreateScope();
        Assert.Equal("Closed", (await verify.ServiceProvider.GetRequiredService<RestaurantDbContext>().TableSessions.SingleAsync()).Status);
    }

    [Fact]
    public async Task ClosureFailureRollsBackSessionAndGuestRelease()
    {
        var payment = await ReviewedSettlement();
        using var scope = _restaurant.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RestaurantDbContext>();
        await db.Database.ExecuteSqlRawAsync("CREATE TRIGGER reject_close BEFORE UPDATE ON TableSession BEGIN SELECT RAISE(ABORT, 'Simulated close failure'); END;");
        using var channel = ScopeChannel();
        var error = await Assert.ThrowsAsync<RpcException>(async () => await new SessionSettlementGrpc.SessionSettlementGrpcClient(channel).ClosePaidSessionAsync(Signed(payment)));
        Assert.Equal(StatusCode.Internal, error.StatusCode);
        Assert.Null((await db.TableSessions.AsNoTracking().SingleAsync()).ClosedAt);
        Assert.Null((await db.SessionTables.AsNoTracking().SingleAsync()).ReleasedAt);
    }

    private async Task<PaidSessionSettlement> ReviewedSettlement()
    {
        var (sessionId, restaurantId) = await SeedPaymentSession();
        var checkout = Guid.NewGuid();
        using var scope = _restaurant.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RestaurantDbContext>();
        db.ServiceRequests.Add(new ServiceRequest
        {
            ServiceRequestId = checkout,
            TableSessionId = sessionId,
            Type = "Payment",
            Status = "Reviewed",
            RequestedAt = DateTime.UtcNow.AddMinutes(-5),
            AcknowledgedAt = DateTime.UtcNow.AddMinutes(-4),
            HandledByRef = Guid.Parse("11111111-1111-1111-1111-111111111111")
        });
        await db.SaveChangesAsync();
        return new PaidSessionSettlement(sessionId, restaurantId, Guid.NewGuid(), checkout, SettlementNow().AddMinutes(-1));
    }

    private DateTimeOffset SettlementNow()
    {
        return _restaurant.Services.GetRequiredService<TimeProvider>().GetUtcNow();
    }

    private SessionPaymentCloseRequest Signed(PaidSessionSettlement payment)
    {
        var timestamp = SettlementNow().ToUnixTimeSeconds();
        return new SessionPaymentCloseRequest
        {
            TableSessionId = payment.TableSessionId.ToString(),
            RestaurantId = payment.RestaurantId.ToString(),
            BillId = payment.BillId.ToString(),
            CheckoutId = payment.CheckoutId.ToString(),
            PaidAt = payment.PaidAt.ToString("O", CultureInfo.InvariantCulture),
            SignatureTime = timestamp,
            Signature = PaymentSettlementSignature.Sign(PaymentSettlementSignature.ProofPurpose,
                PaymentSettlementSignature.Proof(payment), timestamp, SettlementTestKey)
        };
    }

    private sealed class SessionPaymentReader : ISessionPaymentClient
    {
        public PaidSessionSettlement? Payment { get; set; }

        public Task<PaidSessionSettlement?> GetPaidAsync(Guid sessionId, Guid restaurantId, CancellationToken ct)
        {
            return Task.FromResult(Payment?.TableSessionId == sessionId && Payment.RestaurantId == restaurantId ? Payment : null);
        }
    }
}
