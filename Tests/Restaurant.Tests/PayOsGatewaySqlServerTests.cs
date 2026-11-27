extern alias billingapi;

using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ApiGateway.DTOs.Billing;
using Billing.BLL.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shared.Common.Pagination;
using Shared.Persistence.RestaurantIsolation;
using Xunit;
using BillingDb = Billing.DAL.Models.BillingDbContext;
using PayOsCredentials = Billing.Common.DTOs.PayOsCredentials;
using PayOsSignature = billingapi::Billing.API.Clients.PayOsSignature;
using PayOsPaymentDTO = ApiGateway.DTOs.Billing.PayOsPaymentDTO;
using WaiterPayOsPaymentDTO = ApiGateway.DTOs.Billing.WaiterPayOsPaymentDTO;

namespace Restaurant.Tests;

public sealed partial class ReservationSqlServerTests
{
    private async Task VerifyDirectCash(PayOsHttpFixture provider, Guid sessionId, CashShiftDTO shift,
        HttpClient cashier, HttpClient waiter)
    {
        using var tables = JsonDocument.Parse(await cashier.GetStringAsync($"/api/tables/service?restaurantId={shift.RestaurantId}"));
        Assert.Contains(tables.RootElement.GetProperty("result").GetProperty("items").EnumerateArray(),
            table => table.GetProperty("openSessionIds").EnumerateArray().Any(value => value.GetGuid() == sessionId));
        var checkoutPath = $"/api/table-sessions/{sessionId}/checkout";
        var key = Guid.NewGuid();
        async Task<HttpResponseMessage> Prepare(HttpClient client)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, checkoutPath + "/prepare-at-counter");
            request.Headers.Add("Idempotency-Key", key.ToString());
            return await client.SendAsync(request);
        }

        using var forbidden = await Prepare(waiter);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        var prepared = await Read<PreparedResult>(await Prepare(cashier));
        Assert.Equal("Prepared", prepared.CheckoutStatus);
        Assert.Equal(key, prepared.CheckoutId);
        var present = $"{checkoutPath}/{prepared.CheckoutId}/present-at-counter";
        using var noQr = await cashier.PostAsync(present, null);
        Assert.Equal(HttpStatusCode.Conflict, noQr.StatusCode);

        var qr = await Read<PayOsPaymentDTO>(await CreatePayOs(cashier, prepared, shift, Guid.NewGuid()), HttpStatusCode.Created);
        Assert.Equal(PayOsHttpFixture.Qr, qr.QrCode);
        var presented = await Read<PreparedResult>(await cashier.PostAsync(present, null));
        Assert.Equal("Presented", presented.CheckoutStatus);

        var cashKey = Guid.NewGuid();
        async Task<HttpResponseMessage> Cash(decimal received)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/bills/{prepared.Bill.BillId}/payments/cash")
            {
                Content = JsonContent.Create(new { checkoutId = prepared.CheckoutId, cashShiftId = shift.CashShiftId, cashReceived = received })
            };
            request.Headers.Add("Idempotency-Key", cashKey.ToString());
            return await cashier.SendAsync(request);
        }

        using var shortCash = await Cash(prepared.Bill.TotalAmount - 1000);
        Assert.Equal(HttpStatusCode.BadRequest, shortCash.StatusCode);
        using var shortage = JsonDocument.Parse(await shortCash.Content.ReadAsStringAsync());
        Assert.Equal("InsufficientCash", shortage.RootElement.GetProperty("result").GetProperty("status").GetString());
        Assert.Equal(1000m, shortage.RootElement.GetProperty("result").GetProperty("amount").GetDecimal());

        provider.Offline = true;
        using var waiting = await Cash(30000);
        Assert.Equal(HttpStatusCode.Conflict, waiting.StatusCode);
        using var pending = JsonDocument.Parse(await waiting.Content.ReadAsStringAsync());
        Assert.Equal("PendingReconciliation", pending.RootElement.GetProperty("result").GetProperty("status").GetString());
        Assert.Null(await ReadClosedAt(sessionId));

        provider.Offline = false;
        var paid = await Read<PaymentDTO>(await Cash(30000), HttpStatusCode.Created);
        Assert.Equal(1483m, paid.ChangeAmount);
        Assert.Equal(paid.PaymentId, (await Read<PaymentDTO>(await Cash(30000))).PaymentId);
        var closed = await Read<PayOsPaymentDTO>(await cashier.GetAsync($"/api/payments/{qr.PaymentId}/payos"));
        Assert.True(closed.IsVerifiedZero);
        Assert.NotNull(await ReadClosedAt(sessionId));
    }

    private async Task<PreparedResult> VerifyPayOsBeforePresentation(PayOsHttpFixture provider, PreparedResult prepared,
        CashShiftDTO shift, HttpClient cashier, HttpClient oldCashier, HttpClient waiter, HttpClient otherWaiter,
        HttpClient webhookClient, string billPath, string presentPath)
    {
        var key = Guid.NewGuid();
        using var unauthorized = await CreatePayOs(waiter, prepared, shift, key);
        Assert.Equal(HttpStatusCode.Forbidden, unauthorized.StatusCode);
        using var wrongCashier = await CreatePayOs(oldCashier, prepared, shift, key);
        Assert.Equal(HttpStatusCode.Forbidden, wrongCashier.StatusCode);
        var payment = await Read<PayOsPaymentDTO>(await CreatePayOs(cashier, prepared, shift, key), HttpStatusCode.Created);
        Assert.Equal(PayOsHttpFixture.Qr, payment.QrCode);
        Assert.Equal("Pending", payment.Status);
        Assert.Equal(prepared.Bill.TotalAmount, payment.Amount);
        var replay = await Read<PayOsPaymentDTO>(await CreatePayOs(cashier, prepared, shift, key));
        Assert.Equal(payment.PaymentId, replay.PaymentId);
        using var duplicate = await CreatePayOs(cashier, prepared, shift, Guid.NewGuid());
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        var orderCode = Assert.Single(provider.OrderCodes);
        var beforeDelivery = await Read<PreparedResult>(await waiter.GetAsync(billPath));
        Assert.Equal("Prepared", beforeDelivery.CheckoutStatus);
        Assert.Equal("Unpaid", beforeDelivery.Bill.Status);
        Assert.Null(beforeDelivery.PresentedAt);

        var webhook = $"/api/restaurants/{shift.RestaurantId}/payments/payos/webhook";
        var payload = provider.Webhook(orderCode);
        using var unverified = await webhookClient.PostAsJsonAsync(webhook, payload);
        Assert.Equal(HttpStatusCode.OK, unverified.StatusCode);
        var paymentPath = $"/api/payments/{payment.PaymentId}/payos";
        Assert.Equal("Pending", (await Read<PayOsPaymentDTO>(await cashier.GetAsync(paymentPath))).Status);

        var unpaid = await Read<PreparedResult>(await waiter.GetAsync(billPath));
        Assert.Equal("Unpaid", unpaid.Bill.Status);
        Assert.Null(unpaid.PresentedAt);

        provider.Settle(orderCode);
        using var verified = await webhookClient.PostAsJsonAsync(webhook, payload);
        Assert.Equal(HttpStatusCode.OK, verified.StatusCode);
        var paid = await Read<PayOsPaymentDTO>(await cashier.GetAsync(paymentPath));
        Assert.Equal("Paid", paid.Status);
        Assert.NotNull(paid.PaidAt);
        Assert.Null(await ReadClosedAt(prepared.TableSessionId));

        using var wrongWaiter = await otherWaiter.PostAsync(presentPath, null);
        Assert.Equal(HttpStatusCode.Forbidden, wrongWaiter.StatusCode);
        var delivered = await Read<PreparedResult>(await waiter.PostAsync(presentPath, null));
        Assert.Equal("Paid", delivered.CheckoutStatus);
        Assert.Equal("Paid", delivered.Bill.Status);
        Assert.NotNull(delivered.PresentedAt);
        Assert.NotNull(await ReadClosedAt(prepared.TableSessionId));
        Assert.Equivalent(delivered, await Read<PreparedResult>(await waiter.PostAsync(presentPath, null)), strict: true);
        Assert.Single(provider.OrderCodes);
        return delivered;
    }

    private async Task VerifyPayOsPayment(BillingDb db, PayOsHttpFixture provider, PreparedResult presented,
        CashShiftDTO shift, HttpClient cashier, HttpClient oldCashier, HttpClient waiter, HttpClient otherWaiter,
        HttpClient crossRestaurantWaiter, HttpClient guest, Guid otherRestaurant)
    {
        var key = Guid.NewGuid();
        var waiterPath = $"/api/table-sessions/{presented.TableSessionId}/checkout/{presented.CheckoutId}/payments";
        var queue = waiterPath + "/payos";
        using var swagger = JsonDocument.Parse(await cashier.GetStringAsync("/swagger/v1/swagger.json"));
        var paths = swagger.RootElement.GetProperty("paths");
        Assert.True(paths.GetProperty("/api/table-sessions/{sessionId}/checkout/{checkoutId}/payments/payos")
            .TryGetProperty("get", out _));
        Assert.True(paths.GetProperty("/api/restaurants/{restaurantId}/payments/payos/webhook").TryGetProperty("post", out _));
        Assert.Empty((await Read<PagedResult<WaiterPayOsPaymentDTO>>(await waiter.GetAsync(queue))).Items);
        Assert.Equal(HttpStatusCode.Unauthorized, (await guest.GetAsync(queue)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await otherWaiter.GetAsync(queue)).StatusCode);
        var crossRestaurant = await crossRestaurantWaiter.GetAsync(queue);
        Assert.Contains(crossRestaurant.StatusCode, new[] { HttpStatusCode.Forbidden, HttpStatusCode.NotFound });
        Assert.Equal(HttpStatusCode.Forbidden, (await CreatePayOs(waiter, presented, shift, key)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await CreatePayOs(oldCashier, presented, shift, key)).StatusCode);

        provider.Offline = true;
        var unknown = await Read<PayOsPaymentDTO>(await CreatePayOs(cashier, presented, shift, key), HttpStatusCode.Created);
        Assert.Equal("Unknown", unknown.Status);
        Assert.Null(unknown.QrCode);
        Assert.Empty(provider.OrderCodes);
        Assert.Equal(HttpStatusCode.Conflict, (await CreatePayOs(cashier, presented, shift, Guid.NewGuid())).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await CollectCash(waiter, presented.Bill.BillId, presented.CheckoutId,
            30000m, Guid.NewGuid())).StatusCode);

        provider.Offline = false;
        var pending = await Read<PayOsPaymentDTO>(await CreatePayOs(cashier, presented, shift, key));
        Assert.Equal(unknown.PaymentId, pending.PaymentId);
        Assert.Equal(unknown.ProviderOrderCode, pending.ProviderOrderCode);
        Assert.Equal("Pending", pending.Status);
        Assert.Equal(presented.Bill.TotalAmount, pending.Amount);
        Assert.Equal(PayOsHttpFixture.Qr, pending.QrCode);
        Assert.Single(provider.OrderCodes);
        var detail = $"{waiterPath}/{pending.PaymentId}/payos";
        var visible = await Read<WaiterPayOsPaymentDTO>(await waiter.GetAsync(detail));
        Assert.True(visible.CanPresentQr);
        Assert.Equal(pending.QrCode, visible.QrCode);
        Assert.Equal(pending.CheckoutUrl, visible.CheckoutUrl);
        Assert.False(visible.PaymentConfirmed);
        Assert.Equal(pending.PaymentId, Assert.Single((await Read<PagedResult<WaiterPayOsPaymentDTO>>(await waiter.GetAsync(queue))).Items).PaymentId);
        Assert.Equal(HttpStatusCode.NotFound, (await waiter.GetAsync($"{waiterPath}/{Guid.NewGuid()}/payos")).StatusCode);

        var cancel = $"/api/payments/{pending.PaymentId}/payos/cancel";
        Assert.Equal(HttpStatusCode.Forbidden, (await waiter.PostAsync(cancel, null)).StatusCode);
        var cancelled = await Read<PayOsPaymentDTO>(await cashier.PostAsync(cancel, null));
        Assert.True(cancelled.IsVerifiedZero);
        Assert.Null(cancelled.QrCode);
        Assert.False((await Read<WaiterPayOsPaymentDTO>(await waiter.GetAsync(detail))).CanPresentQr);

        var second = await Read<PayOsPaymentDTO>(await CreatePayOs(cashier, presented, shift, Guid.NewGuid()),
            HttpStatusCode.Created);
        Assert.NotEqual(pending.PaymentId, second.PaymentId);
        Assert.Equal(2, provider.OrderCodes.Count);
        var finalDetail = $"{waiterPath}/{second.PaymentId}/payos";
        var orderCode = long.Parse(second.ProviderOrderCode!, CultureInfo.InvariantCulture);
        var webhook = $"/api/restaurants/{shift.RestaurantId}/payments/payos/webhook";
        Assert.Equal(HttpStatusCode.BadRequest, (await guest.PostAsJsonAsync(webhook,
            provider.Webhook(orderCode, validSignature: false))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await guest.PostAsJsonAsync($"/api/restaurants/{otherRestaurant}/payments/payos/webhook",
            provider.Webhook(orderCode))).StatusCode);
        Assert.Empty(await db.PaymentWebhooks.AsNoTracking().ToListAsync());

        var payload = provider.Webhook(orderCode);
        Assert.Equal(HttpStatusCode.OK, (await guest.PostAsJsonAsync(webhook, payload)).StatusCode);
        var waitingForVerification = await Read<WaiterPayOsPaymentDTO>(await waiter.GetAsync(finalDetail));
        Assert.False(waitingForVerification.CanPresentQr);
        Assert.False(waitingForVerification.PaymentConfirmed);
        Assert.Null(waitingForVerification.QrCode);
        Assert.Equal("Unpaid", waitingForVerification.BillStatus);
        Assert.Null(await ReadClosedAt(presented.TableSessionId));

        var shiftPath = $"/api/cash-shifts/{shift.CashShiftId}";
        var closeBody = new { actualCash = shift.OpeningCash, cashHandleOver = 0 };
        var pendingReconciliation = await Read<ShiftReconciliationDTO>(await cashier.GetAsync(shiftPath + "/reconciliation"));
        Assert.False(pendingReconciliation.CanClose);
        Assert.Equal(0, pendingReconciliation.BankTransferTotal);
        Assert.Contains(pendingReconciliation.Blockers,
            blocker => blocker.Code == "UnresolvedPayOsPayment" && blocker.PaymentId == second.PaymentId);
        using var pendingClose = await cashier.PostAsJsonAsync(shiftPath + "/close", closeBody);
        Assert.Equal(HttpStatusCode.Conflict, pendingClose.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await cashier.GetAsync(shiftPath + "/report")).StatusCode);

        var reportedCash = await Read<CashHandoverDTO>(
            await CollectCash(waiter, presented.Bill.BillId, presented.CheckoutId, 30000m, Guid.NewGuid()),
            HttpStatusCode.Created);
        Assert.Equal("Pending", reportedCash.Status);
        Assert.Equal("Unpaid", (await db.Bills.AsNoTracking().SingleAsync(value => value.BillId == presented.Bill.BillId)).Status);
        var confirmPath = $"/api/cash-handovers/{reportedCash.PaymentId}/confirm";
        provider.Settle(orderCode);
        var cashAttempt = cashier.PostAsJsonAsync(confirmPath, new { confirmedAmount = 30000m });
        var deliveries = await Task.WhenAll(guest.PostAsJsonAsync(webhook, payload),
            guest.PostAsJsonAsync("/api/payments/payos/webhook", payload));
        foreach (var response in deliveries)
        {
            using (response)
            {
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            }
        }

        using var firstCashResult = await cashAttempt;
        Assert.Equal(HttpStatusCode.Conflict, firstCashResult.StatusCode);
        using var retriedCash = await cashier.PostAsJsonAsync(confirmPath, new { confirmedAmount = 30000m });
        Assert.Equal(HttpStatusCode.Conflict, retriedCash.StatusCode);
        using var returnBody = JsonDocument.Parse(await retriedCash.Content.ReadAsStringAsync());
        Assert.Equal("CashReturnRequired", returnBody.RootElement.GetProperty("result").GetProperty("status").GetString());
        Assert.Equal(30000m, returnBody.RootElement.GetProperty("result").GetProperty("amount").GetDecimal());
        Assert.Equal("ReturnRequired", (await Read<CashHandoverDTO>(await cashier.GetAsync(
            $"/api/cash-handovers/{reportedCash.PaymentId}"))).Status);
        Assert.Empty(await db.CashMovements.AsNoTracking().ToListAsync());

        var verified = await Read<WaiterPayOsPaymentDTO>(await waiter.GetAsync(finalDetail));
        Assert.Equal("Paid", verified.Status);
        Assert.Equal("Paid", verified.BillStatus);
        Assert.True(verified.PaymentConfirmed);
        Assert.False(verified.CanPresentQr);
        Assert.Null(verified.QrCode);
        var cashierResult = await Read<PayOsPaymentDTO>(await cashier.GetAsync($"/api/payments/{second.PaymentId}/payos"));
        Assert.Equal("Paid", cashierResult.Status);
        Assert.Null(cashierResult.QrCode);

        var paidAt = cashierResult.PaidAt;
        var closedAt = await ReadClosedAt(presented.TableSessionId);
        Assert.NotNull(paidAt);
        Assert.NotNull(closedAt);
        _clock.Now = _clock.Now.AddMinutes(1);
        var queryCount = provider.QueryCount;
        Assert.Equal(HttpStatusCode.OK, (await guest.PostAsJsonAsync(webhook, payload)).StatusCode);
        Assert.Equal(queryCount, provider.QueryCount);
        Assert.Equal(closedAt, await ReadClosedAt(presented.TableSessionId));
        Assert.Equal(3, await db.Payments.AsNoTracking().CountAsync());
        var paid = await db.Payments.AsNoTracking().SingleAsync(value => value.Status == "Paid");
        Assert.Equal(second.PaymentId, paid.PaymentId);
        Assert.Equal("PayOSQuery", paid.VerificationSource);
        Assert.Equal(paidAt.Value.UtcDateTime, paid.PaidAt);
        var paidBill = await db.Bills.AsNoTracking().SingleAsync(value => value.BillId == paid.BillId);
        Assert.Equal("Paid", paidBill.Status);
        Assert.Equal(paid.PaidAt, paidBill.PaidAt);
        await AssertPaidCashierBoard(cashier, shift.RestaurantId, shift.CashShiftId, paidBill.BillId, paidBill.TotalAmount);
        Assert.Equal("Paid", (await db.BillCheckouts.AsNoTracking().SingleAsync(value => value.CheckoutId == presented.CheckoutId)).Status);
        Assert.Equal("Applied", (await db.PaymentWebhooks.AsNoTracking().SingleAsync()).ProcessingStatus);
        Assert.Empty(await db.CashMovements.AsNoTracking().ToListAsync());
        var recordedReturn = await db.CashHandovers.AsNoTracking().SingleAsync();
        Assert.Equal(reportedCash.PaymentId, recordedReturn.PaymentId);
        Assert.Equal("ReturnRequired", recordedReturn.Status);
        Assert.Null(recordedReturn.ConfirmedAt);

        var paidReconciliation = await Read<ShiftReconciliationDTO>(await cashier.GetAsync(shiftPath + "/reconciliation"));
        Assert.True(paidReconciliation.CanClose);
        Assert.Equal(presented.Bill.TotalAmount, paidReconciliation.BankTransferTotal);
        Assert.Equal(shift.OpeningCash, paidReconciliation.ExpectedCash);
        Assert.Equal(0, paidReconciliation.SaleTotal);
        using var wrongOwner = await oldCashier.PostAsJsonAsync(shiftPath + "/close", closeBody);
        Assert.Equal(HttpStatusCode.Forbidden, wrongOwner.StatusCode);
        provider.Offline = true;
        using var unavailableClose = await cashier.PostAsJsonAsync(shiftPath + "/close", closeBody);
        Assert.Equal(HttpStatusCode.Conflict, unavailableClose.StatusCode);
        Assert.Equal("Active", (await Read<ShiftReconciliationDTO>(await cashier.GetAsync(shiftPath + "/reconciliation"))).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await cashier.GetAsync(shiftPath + "/report")).StatusCode);

        provider.Offline = false;
        var report = await Read<ShiftReportDTO>(await cashier.PostAsJsonAsync(shiftPath + "/close", closeBody), HttpStatusCode.Created);
        Assert.Equal(shift.OpeningCash, report.ExpectedCash);
        Assert.Equal(0, report.Difference);
        var queriesAfterClose = provider.QueryCount;
        var replayReport = await Read<ShiftReportDTO>(await cashier.PostAsJsonAsync(shiftPath + "/close", closeBody));
        Assert.Equivalent(report, replayReport, strict: true);
        Assert.Equal(queriesAfterClose, provider.QueryCount);
    }

    private async Task VerifyPayOsWebhookUnderReview(BillingDb db, PayOsHttpFixture provider, PreparedResult presented,
        CashShiftDTO shift, HttpClient cashier, HttpClient webhookClient, HttpClient guest)
    {
        var pending = await Read<PayOsPaymentDTO>(await CreatePayOs(cashier, presented, shift, Guid.NewGuid()),
            HttpStatusCode.Created);
        var orderCode = long.Parse(pending.ProviderOrderCode!, CultureInfo.InvariantCulture);
        provider.Settle(orderCode);
        provider.QueryAmountOverride = (long)pending.Amount + 1;

        var reconcile = $"/api/payments/{pending.PaymentId}/payos/reconcile";
        var mismatched = await Read<PayOsPaymentDTO>(await cashier.PostAsync(reconcile, null));
        Assert.Equal("ReviewRequired", mismatched.Status);
        Assert.Null(mismatched.PaidAt);
        Assert.Empty(await db.PaymentWebhooks.AsNoTracking().ToListAsync());

        provider.QueryAmountOverride = null;
        var queryCount = provider.QueryCount;
        var webhook = $"/api/restaurants/{shift.RestaurantId}/payments/payos/webhook";
        var payload = provider.Webhook(orderCode);
        using var first = await webhookClient.PostAsJsonAsync(webhook, payload);
        using var duplicate = await webhookClient.PostAsJsonAsync(webhook, payload);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, duplicate.StatusCode);

        var audit = await db.PaymentWebhooks.AsNoTracking().SingleAsync();
        Assert.Equal(pending.PaymentId, audit.PaymentId);
        Assert.Equal("ReviewRequired", audit.ProcessingStatus);
        Assert.NotNull(audit.ProcessedAt);
        var payment = await db.Payments.AsNoTracking().SingleAsync();
        Assert.Equal("ReviewRequired", payment.Status);
        Assert.Null(payment.PaidAt);
        Assert.Equal("Unpaid", (await db.Bills.AsNoTracking().SingleAsync(bill => bill.BillId == presented.Bill.BillId)).Status);
        var checkout = await db.BillCheckouts.AsNoTracking().SingleAsync(value => value.CheckoutId == presented.CheckoutId);
        Assert.Equal("Presented", checkout.Status);
        Assert.Null(checkout.ReleasedAt);
        Assert.Empty(await db.CashMovements.AsNoTracking().ToListAsync());
        Assert.Null(await ReadClosedAt(presented.TableSessionId));
        using var currentGuest = await guest.GetAsync($"/api/table-sessions/{presented.TableSessionId}/guest-access/me");
        Assert.Equal(HttpStatusCode.OK, currentGuest.StatusCode);
        Assert.Equal(queryCount, provider.QueryCount);
    }

    private async Task VerifyConflictingPayOsWebhook(BillingDb db, PayOsHttpFixture provider, PreparedResult presented,
        CashShiftDTO shift, HttpClient cashier, HttpClient webhookClient)
    {
        var pending = await Read<PayOsPaymentDTO>(await CreatePayOs(cashier, presented, shift, Guid.NewGuid()),
            HttpStatusCode.Created);
        var orderCode = long.Parse(pending.ProviderOrderCode!, CultureInfo.InvariantCulture);
        var webhook = $"/api/restaurants/{shift.RestaurantId}/payments/payos/webhook";
        var original = provider.Webhook(orderCode);
        using var first = await webhookClient.PostAsJsonAsync(webhook, original);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var waiting = await db.PaymentWebhooks.AsNoTracking().SingleAsync();
        Assert.Equal("AwaitingProvider", waiting.ProcessingStatus);
        Assert.Null(waiting.ProcessedAt);

        var queryCount = provider.QueryCount;
        using var conflict = await webhookClient.PostAsJsonAsync(webhook, provider.Webhook(orderCode, amount: (long)pending.Amount + 1));
        Assert.Equal(HttpStatusCode.OK, conflict.StatusCode);
        var audits = await db.PaymentWebhooks.AsNoTracking().ToListAsync();
        Assert.Equal(2, audits.Count);
        Assert.All(audits, audit =>
        {
            Assert.Equal("ReviewRequired", audit.ProcessingStatus);
            Assert.NotNull(audit.ProcessedAt);
        });

        using var retry = await webhookClient.PostAsJsonAsync(webhook, original);
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        Assert.Equal(2, await db.PaymentWebhooks.AsNoTracking().CountAsync());
        Assert.Equal(queryCount, provider.QueryCount);
        var payment = await db.Payments.AsNoTracking().SingleAsync();
        Assert.Equal("ReviewRequired", payment.Status);
        Assert.Null(payment.PaidAt);
        Assert.Equal("Unpaid", (await db.Bills.AsNoTracking().SingleAsync(bill => bill.BillId == presented.Bill.BillId)).Status);
        Assert.Null(await ReadClosedAt(presented.TableSessionId));
    }

    private static async Task SeedPayOsConfiguration(IServiceProvider services, params Guid[] restaurantIds)
    {
        for (var index = 0; index < restaurantIds.Length; index++)
        {
            var restaurantId = restaurantIds[index];
            using var scope = services.CreateScope();
            scope.ServiceProvider.GetRequiredService<RestaurantDataScope>().Select(restaurantId);
            var db = scope.ServiceProvider.GetRequiredService<BillingDb>();
            var secrets = scope.ServiceProvider.GetRequiredService<IPaymentProviderSecrets>();
            db.PaymentProviderConfigs.Add(new Billing.DAL.Models.PaymentProviderConfig
            {
                PaymentProviderConfigId = Guid.NewGuid(),
                RestaurantId = restaurantId,
                Provider = "PayOS",
                Enabled = true,
                ClientId = "gateway-test",
                SecretReference = secrets.Protect(restaurantId, new PayOsCredentials
                {
                    ApiKey = "gateway-test",
                    ChecksumKey = PayOsHttpFixture.Checksum
                }),
                ReturnUrl = "https://restaurant.example/payment/return",
                CancelUrl = "https://restaurant.example/payment/cancel",
                WebhookUrl = $"https://restaurant.example/api/restaurants/{restaurantId}/payments/payos/webhook"
            });
            if (index == 0)
            {
                db.PaymentMethods.AddRange(
                    new Billing.DAL.Models.PaymentMethod { MethodCode = "BankTransfer", IsEnabled = true },
                    new Billing.DAL.Models.PaymentMethod { MethodCode = "Cash", IsEnabled = true });
            }

            await db.SaveChangesAsync();
        }
    }

    private static async Task<HttpResponseMessage> CreatePayOs(HttpClient client, PreparedResult bill, CashShiftDTO shift, Guid key)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/bills/{bill.Bill.BillId}/payments/payos")
        {
            Content = JsonContent.Create(new { checkoutId = bill.CheckoutId, cashShiftId = shift.CashShiftId })
        };
        request.Headers.Add("Idempotency-Key", key.ToString());
        return await client.SendAsync(request);
    }

    private sealed class PayOsHttpFixture : HttpMessageHandler
    {
        public const string Checksum = "gateway-test-pay-os-checksum";
        public const string Qr = "GatewayTestQr";
        private readonly ConcurrentDictionary<long, Transfer> orders = new();
        private int queryCount;
        private int createCount;
        public bool Offline { get; set; }
        public bool LoseCreateResponse { get; set; }
        public bool LoseCancelResponse { get; set; }
        public bool LookupTimesOut { get; set; }
        public long? QueryAmountOverride { get; set; }
        public int QueryCount => Volatile.Read(ref queryCount);
        public int CreateCount => Volatile.Read(ref createCount);
        public ICollection<long> OrderCodes => orders.Keys;

        public void Settle(long code)
        {
            orders[code] = orders[code] with { Status = "PAID" };
        }

        public JsonElement Webhook(long orderCode, bool validSignature = true, long? amount = null)
        {
            var row = orders[orderCode];
            var data = JsonSerializer.SerializeToElement(new
            {
                orderCode,
                amount = amount ?? row.Amount,
                reference = Reference(orderCode),
                paymentLinkId = row.LinkId,
                currency = "VND",
                code = "00"
            });
            return JsonSerializer.SerializeToElement(new
            {
                code = "00",
                success = true,
                data,
                signature = validSignature ? PayOsSignature.SignData(data, Checksum) : new string('0', 64)
            });
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            const string path = "/v2/payment-requests";
            if (Offline)
            {
                return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
            }

            Assert.Equal("gateway-test", Assert.Single(request.Headers.GetValues("x-client-id")));
            Assert.Equal("gateway-test", Assert.Single(request.Headers.GetValues("x-api-key")));
            var route = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Post && route == path)
            {
                Interlocked.Increment(ref createCount);
                var create = await request.Content!.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
                var code = create.GetProperty("orderCode").GetInt64();
                var amount = create.GetProperty("amount").GetInt64();
                VerifyCreateSignature(create, code, amount);
                var row = orders.GetOrAdd(code, _ => new Transfer(amount, Guid.NewGuid().ToString("N"), "PENDING"));
                if (LoseCreateResponse)
                {
                    LookupTimesOut = true;
                    await Task.Delay(Timeout.InfiniteTimeSpan, ct);
                }

                return Signed(new
                {
                    orderCode = code,
                    amount,
                    status = row.Status,
                    currency = "VND",
                    paymentLinkId = row.LinkId,
                    checkoutUrl = "https://pay-os.example/checkout/" + row.LinkId,
                    qrCode = Qr
                });
            }

            var segments = route.Split('/');
            var orderCode = long.Parse(segments[3], CultureInfo.InvariantCulture);
            if (!orders.TryGetValue(orderCode, out var status))
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }

            if (request.Method == HttpMethod.Post && segments.Length == 5 && segments[4] == "cancel")
            {
                status = status with { Status = "CANCELLED" };
                orders[orderCode] = status;
                if (LoseCancelResponse)
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, ct);
                }
            }

            if (request.Method == HttpMethod.Get)
            {
                Interlocked.Increment(ref queryCount);
                if (LookupTimesOut)
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, ct);
                }
            }

            var settled = status.Status == "PAID";
            var queryAmount = QueryAmountOverride ?? status.Amount;
            return Signed(new
            {
                orderCode,
                id = status.LinkId,
                amount = queryAmount,
                amountPaid = settled ? queryAmount : 0,
                amountRemaining = settled ? 0 : queryAmount,
                status = status.Status,
                transactions = settled ? new[] { new { reference = Reference(orderCode), amount = queryAmount } } : []
            });
        }

        private static void VerifyCreateSignature(JsonElement body, long code, long amount)
        {
            var expected = PayOsSignature.SignPaymentRequest(code, amount, body.GetProperty("description").GetString()!,
                body.GetProperty("cancelUrl").GetString()!, body.GetProperty("returnUrl").GetString()!, Checksum);
            Assert.Equal(expected, body.GetProperty("signature").GetString());
        }

        private static HttpResponseMessage Signed(object value)
        {
            var data = JsonSerializer.SerializeToElement(value);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { code = "00", data, signature = PayOsSignature.SignData(data, Checksum) })
            };
        }

        private static string Reference(long code)
        {
            return $"TEST-{code}";
        }

        private sealed record Transfer(long Amount, string LinkId, string Status);
    }
}
