extern alias billingapi;

using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ApiGateway.DTOs.Billing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using BillingDb = Billing.DAL.Models.BillingDbContext;
using PayOsWorker = billingapi::Billing.API.Workers.PayOsReconciliationWorker;

namespace Restaurant.Tests;

public sealed partial class ReservationSqlServerTests
{
    private async Task VerifyPayOsTimeoutRecovery(IServiceProvider services, BillingDb db, PayOsHttpFixture provider,
        PreparedResult presented, CashShiftDTO shift, HttpClient cashier, HttpClient waiter, HttpClient guest)
    {
        provider.LoseCreateResponse = true;
        var key = Guid.NewGuid();
        var unknown = await Read<PayOsPaymentDTO>(await CreatePayOs(cashier, presented, shift, key),
            HttpStatusCode.Created);
        Assert.Equal("Unknown", unknown.Status);
        Assert.False(unknown.IsVerifiedZero);
        Assert.Null(unknown.QrCode);
        var orderCode = long.Parse(unknown.ProviderOrderCode!, CultureInfo.InvariantCulture);
        Assert.Equal(orderCode, Assert.Single(provider.OrderCodes));
        Assert.Equal(1, provider.CreateCount);
        await AssertPaymentStillHeld(db, presented, shift, cashier, waiter, guest);
        Assert.Equal(1, provider.CreateCount);
        Assert.Single(await db.Payments.AsNoTracking().ToListAsync());

        provider.LoseCreateResponse = false;
        provider.LookupTimesOut = false;
        var recovered = await RecoverPayOsWithWorker(services, cashier, unknown.PaymentId, presented.TableSessionId, "Pending");
        Assert.Equal(unknown.PaymentId, recovered.PaymentId);
        Assert.Equal(unknown.ProviderOrderCode, recovered.ProviderOrderCode);
        Assert.NotNull(recovered.ProviderPaymentLinkId);
        Assert.Null(recovered.QrCode);
        Assert.Null(recovered.CheckoutUrl);
        var replay = await Read<PayOsPaymentDTO>(await CreatePayOs(cashier, presented, shift, key));
        Assert.Equal(unknown.PaymentId, replay.PaymentId);
        Assert.Equal(1, provider.CreateCount);

        provider.LoseCancelResponse = true;
        var cancel = $"/api/payments/{unknown.PaymentId}/payos/cancel";
        var cancelling = await Read<PayOsPaymentDTO>(await cashier.PostAsync(cancel, null));
        Assert.Equal("CancelRequested", cancelling.Status);
        Assert.False(cancelling.IsVerifiedZero);
        await AssertPaymentStillHeld(db, presented, shift, cashier, waiter, guest);
        Assert.Single(await db.Payments.AsNoTracking().ToListAsync());
        Assert.Equal(1, provider.CreateCount);

        var cancelled = await RecoverPayOsWithWorker(services, cashier, unknown.PaymentId, presented.TableSessionId, "Cancelled");
        Assert.True(cancelled.IsVerifiedZero);
        Assert.Equal(unknown.ProviderOrderCode, cancelled.ProviderOrderCode);
        Assert.Null(await ReadClosedAt(presented.TableSessionId));
        Assert.Null((await db.BillCheckouts.AsNoTracking()
            .SingleAsync(value => value.CheckoutId == presented.CheckoutId)).ReleasedAt);

        var replacement = await Read<PayOsPaymentDTO>(await CreatePayOs(cashier, presented, shift, Guid.NewGuid()),
            HttpStatusCode.Created);
        Assert.NotEqual(unknown.PaymentId, replacement.PaymentId);
        Assert.NotEqual(unknown.ProviderOrderCode, replacement.ProviderOrderCode);
        Assert.Equal(2, provider.CreateCount);
        provider.Settle(long.Parse(replacement.ProviderOrderCode!, CultureInfo.InvariantCulture));
        var paid = await RecoverPayOsWithWorker(services, cashier, replacement.PaymentId, presented.TableSessionId, "Paid");
        Assert.Equal(replacement.ProviderOrderCode, paid.ProviderOrderCode);
        Assert.Equal(2, provider.CreateCount);
        var attempts = await db.Payments.AsNoTracking().ToListAsync();
        Assert.Equal(2, attempts.Count);
        Assert.Equal(replacement.PaymentId, Assert.Single(attempts, value => value.Status == "Paid").PaymentId);
        Assert.Empty(await db.PaymentWebhooks.AsNoTracking().ToListAsync());
        Assert.Empty(await db.CashMovements.AsNoTracking().ToListAsync());
    }

    private async Task AssertPaymentStillHeld(BillingDb db, PreparedResult presented, CashShiftDTO shift,
        HttpClient cashier, HttpClient waiter, HttpClient guest)
    {
        using var anotherAttempt = await CreatePayOs(cashier, presented, shift, Guid.NewGuid());
        Assert.Equal(HttpStatusCode.Conflict, anotherAttempt.StatusCode);
        using var cash = await CollectCash(waiter, presented.Bill.BillId, presented.CheckoutId,
            30000m, Guid.NewGuid());
        Assert.Equal(HttpStatusCode.Conflict, cash.StatusCode);

        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/table-sessions/{presented.TableSessionId}/orders")
        {
            Content = JsonContent.Create(new
            {
                cartId = Guid.NewGuid(),
                cartVersion = Convert.ToBase64String(new byte[8]),
                items = Array.Empty<object>()
            })
        };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        using var addOrder = await guest.SendAsync(request);
        Assert.Equal(HttpStatusCode.Conflict, addOrder.StatusCode);
        using var error = JsonDocument.Parse(await addOrder.Content.ReadAsStringAsync());
        Assert.Contains("Không thể đặt thêm món mới", error.RootElement.GetProperty("message").GetString()!);

        var bill = await db.Bills.AsNoTracking().SingleAsync(value => value.BillId == presented.Bill.BillId);
        Assert.Equal("Unpaid", bill.Status);
        var checkout = await db.BillCheckouts.AsNoTracking().SingleAsync(value => value.CheckoutId == presented.CheckoutId);
        Assert.Equal("Presented", checkout.Status);
        Assert.Null(checkout.ReleasedAt);
        Assert.Null(await ReadClosedAt(presented.TableSessionId));
    }

    private async Task<PayOsPaymentDTO> RecoverPayOsWithWorker(IServiceProvider services, HttpClient cashier,
        Guid paymentId, Guid sessionId, string expectedStatus)
    {
        using var worker = ActivatorUtilities.CreateInstance<PayOsWorker>(services);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await worker.StartAsync(timeout.Token);
        try
        {
            while (true)
            {
                using var response = await cashier.GetAsync($"/api/payments/{paymentId}/payos", timeout.Token);
                var payment = await Read<PayOsPaymentDTO>(response);
                if (payment.Status == expectedStatus && (expectedStatus != "Paid" || (await ReadClosedAt(sessionId)).HasValue))
                {
                    return payment;
                }

                await Task.Delay(TimeSpan.FromMilliseconds(100), timeout.Token);
            }
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }
    }
}
