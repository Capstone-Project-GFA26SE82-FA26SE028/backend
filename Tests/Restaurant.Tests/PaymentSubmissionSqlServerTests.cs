extern alias billingapi;
extern alias orderingapi;
extern alias restaurantapi;

using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using ApiGateway.DTOs.Billing;
using Backend.Contracts.Billing;
using Billing.BLL.Interfaces;
using Xunit;
using BillingDb = Billing.DAL.Models.BillingDbContext;
using BillingGrpc = billingapi::Billing.API.GrpcServices.PaymentSubmissionGrpcService;
using BillingRestaurantAccess = billingapi::Backend.Contracts.Restaurant.RestaurantAccessGrpc.RestaurantAccessGrpcClient;
using BillingServiceRequest = billingapi::Backend.Contracts.Restaurant.ServiceRequestGrpc.ServiceRequestGrpcClient;
using BillingOrderClient = billingapi::Backend.Contracts.Ordering.BillingOrderGrpc.BillingOrderGrpcClient;
using BillingCheckoutLockClient = billingapi::Backend.Contracts.Ordering.CheckoutLockGrpc.CheckoutLockGrpcClient;
using BillingClosureClient = billingapi::Backend.Contracts.Restaurant.PaidSessionClosureGrpc.PaidSessionClosureGrpcClient;
using BillingDisplayClient = billingapi::Backend.Contracts.Restaurant.BillingSessionGrpc.BillingSessionGrpcClient;
using PayOsClient = billingapi::Billing.API.Clients.PayOsClient;
using OrderingDb = Ordering.DAL.Models.OrderingDbContext;
using OrderingModels = Ordering.DAL.Models;
using OrderingGrpc = orderingapi::Ordering.API.GrpcServices.BillingOrderGrpcService;
using OrderingRestaurantAccess = orderingapi::Backend.Contracts.Restaurant.RestaurantAccessGrpc.RestaurantAccessGrpcClient;
using OrderingIdentityUser = orderingapi::Backend.Contracts.Identity.UserGrpc.UserGrpcClient;
using OrderingTableSession = orderingapi::Backend.Contracts.Restaurant.TableSessionGrpc.TableSessionGrpcClient;
using OrderingBillingSession = orderingapi::Backend.Contracts.Restaurant.BillingSessionGrpc.BillingSessionGrpcClient;
using ReviewOrderingGrpc = restaurantapi::Backend.Contracts.Ordering.BillingOrderGrpc.BillingOrderGrpcClient;
using VerifiedCheckoutClient = restaurantapi::Backend.Contracts.Billing.PaymentSettlementGrpc.PaymentSettlementGrpcClient;
using ReviewRestaurantGrpc = Backend.Contracts.Restaurant.ServiceRequestGrpc.ServiceRequestGrpcClient;
using RestaurantMarker = restaurantapi::Restaurant.API.ApiAssembly;

namespace Restaurant.Tests;

public sealed partial class ReservationSqlServerTests
{
    [SqlServerFact]
    public Task PaymentSubmissionTransfersToNextCashierThenPreparesAndPresentsBillThroughGateway()
    {
        return VerifyPaymentSubmission();
    }

    [SqlServerFact]
    public Task PaymentSubmissionCreatesPayOsQrAndSettlesOnlyAfterVerifiedWebhookThroughGateway()
    {
        return VerifyPaymentSubmission(PayOsScenario.Settlement);
    }

    [SqlServerFact]
    public Task PaymentSubmissionCreatesPayOsQrBeforePresentationAndRecordsDeliveryAfterEarlyPayment()
    {
        return VerifyPaymentSubmission(PayOsScenario.BeforePresentation);
    }

    [SqlServerFact]
    public Task PaymentSubmissionKeepsPayOsWebhookUnderReviewWithoutRequeryingProvider()
    {
        return VerifyPaymentSubmission(PayOsScenario.Review);
    }

    [SqlServerFact]
    public Task PaymentSubmissionFinalizesPendingPayOsAuditWhenSignedCallbackConflicts()
    {
        return VerifyPaymentSubmission(PayOsScenario.ConflictingWebhook);
    }

    [SqlServerFact]
    public Task PaymentSubmissionRecoversTimedOutPayOsCreateAndCancelWithoutDuplicateCharges()
    {
        return VerifyPaymentSubmission(PayOsScenario.TimeoutRecovery);
    }

    [SqlServerFact]
    public Task PaymentSubmissionRecoversPaidSessionWhenFirstClosureFails()
    {
        return VerifyPaymentSubmission(recoverClosure: true);
    }

    private async Task VerifyPaymentSubmission(PayOsScenario? payOsScenario = null, bool recoverClosure = false)
    {
        var location = await BillingLocation();
        var otherRestaurant = await BillingLocation(assignStaff: false);
        await AddSecondPaymentWaiter(otherRestaurant.RestaurantId);
        await AddPaymentStaff("nextCashier", "cashier", location.RestaurantId);
        await AddPaymentStaff("sameRestaurantWaiter", "waiter", location.RestaurantId);
        await AddPaymentStaff("paymentKitchen", "kitchen", location.RestaurantId);
        var session = await SeedOpenPaymentSession(location);
        var handoverSession = await PaymentSessionAtAnotherTable(location, "T2");
        var unpaidSession = await PaymentSessionAtAnotherTable(location, "T3");
        var paidSession = await PaymentSessionAtAnotherTable(location, "T4");
        var id = Guid.NewGuid();
        var path = PaymentRequestPath(session);
        var review = path + $"/{id}/review";
        var submit = path + $"/{id}/submit";
        var inbox = $"/api/restaurants/{location.RestaurantId}/payment-requests/incoming";
        var open = $"/api/restaurants/{location.RestaurantId}/cash-shifts/open";
        using var guest = await JoinPaymentSession(session);
        await Read<PaymentRequestResult>(await PostPaymentRequest(guest, path, id));

        var orderItemId = Guid.NewGuid();
        var kitchenTaskId = Guid.NewGuid();
        var server = Environment.GetEnvironmentVariable("RESTAURANT_SQL_TEST_SERVER")!;
        var billingConnection = Connection(server, "TestB2Billing_" + Guid.NewGuid().ToString("N"));
        var orderingConnection = Connection(server, "TestB2Ordering_" + Guid.NewGuid().ToString("N"));
        try
        {
            await CreatePaymentSubmissionSchema(billingConnection, "Billing");
            await CreatePaymentSubmissionSchema(orderingConnection, "Ordering");
            await using var ordering = CreatePaymentSubmissionOrderingHost(orderingConnection);
            var pendingId = await SeedPaymentOrders(ordering, location, session, orderItemId, kitchenTaskId);

            WebApplicationFactory<BillingGrpc>? billingTarget = null;
            await using var reviewingRestaurant = _restaurant.WithWebHostBuilder(builder =>
            {
                builder.UseSetting("GrpcServices:Ordering", "http://ordering.test");
                builder.UseSetting("GrpcServices:Billing", "http://billing.test");
                builder.UseSetting("Notifications:ServiceKey", ClosureKey);
                builder.ConfigureTestServices(services =>
                {
                    services.RemoveAll<IHostedService>();
                    services.AddGrpcClient<ReviewOrderingGrpc>()
                        .ConfigurePrimaryHttpMessageHandler(() => ordering.Server.CreateHandler());
                    services.AddGrpcClient<VerifiedCheckoutClient>()
                        .ConfigurePrimaryHttpMessageHandler(() => billingTarget!.Server.CreateHandler());
                });
            });
            var payOs = payOsScenario.HasValue ? new PayOsHttpFixture() : null;
            await using var billing = CreatePaymentSubmissionBillingHost(billingConnection, reviewingRestaurant, ordering, payOs, recoverClosure);
            billingTarget = billing;
            if (payOs is not null)
            {
                await SeedPayOsConfiguration(billing.Services, location.RestaurantId, otherRestaurant.RestaurantId);
            }

            await using var gateway = _gateway.WithWebHostBuilder(builder =>
            {
                builder.UseSetting("GrpcServices:Billing", "http://billing.test");
                builder.UseSetting("GrpcServices:Ordering", "http://ordering.test");
                builder.UseSetting("Swagger:Enabled", "true");
                builder.ConfigureTestServices(services =>
                {
                    services.AddGrpcClient<ReviewRestaurantGrpc>()
                        .ConfigurePrimaryHttpMessageHandler(() => reviewingRestaurant.Server.CreateHandler());
                    services.AddGrpcClient<Backend.Contracts.Restaurant.TableSessionGrpc.TableSessionGrpcClient>()
                        .ConfigurePrimaryHttpMessageHandler(() => reviewingRestaurant.Server.CreateHandler());
                    services.AddGrpcClient<Backend.Contracts.Ordering.OrderGrpc.OrderGrpcClient>()
                        .ConfigurePrimaryHttpMessageHandler(() => ordering.Server.CreateHandler());
                    services.AddGrpcClient<Backend.Contracts.Ordering.KitchenTaskGrpc.KitchenTaskGrpcClient>()
                        .ConfigurePrimaryHttpMessageHandler(() => ordering.Server.CreateHandler());
                    services.AddGrpcClient<PaymentSubmissionGrpc.PaymentSubmissionGrpcClient>()
                        .ConfigurePrimaryHttpMessageHandler(() => billing.Server.CreateHandler());
                    services.AddGrpcClient<BillCheckoutGrpc.BillCheckoutGrpcClient>()
                        .ConfigurePrimaryHttpMessageHandler(() => billing.Server.CreateHandler());
                    services.AddGrpcClient<PaymentGrpc.PaymentGrpcClient>()
                        .ConfigurePrimaryHttpMessageHandler(() => billing.Server.CreateHandler());
                    services.AddGrpcClient<CashHandoverGrpc.CashHandoverGrpcClient>()
                        .ConfigurePrimaryHttpMessageHandler(() => billing.Server.CreateHandler());
                    services.AddGrpcClient<PayOsPaymentGrpc.PayOsPaymentGrpcClient>()
                        .ConfigurePrimaryHttpMessageHandler(() => billing.Server.CreateHandler());
                    services.AddGrpcClient<PaymentWebhookGrpc.PaymentWebhookGrpcClient>()
                        .ConfigurePrimaryHttpMessageHandler(() => billing.Server.CreateHandler());
                    services.AddGrpcClient<ShiftReportGrpc.ShiftReportGrpcClient>()
                        .ConfigurePrimaryHttpMessageHandler(() => billing.Server.CreateHandler());
                    services.AddGrpcClient<CashRegisterGrpc.CashRegisterGrpcClient>()
                        .ConfigurePrimaryHttpMessageHandler(() => billing.Server.CreateHandler());
                    services.AddGrpcClient<CashShiftGrpc.CashShiftGrpcClient>()
                        .ConfigurePrimaryHttpMessageHandler(() => billing.Server.CreateHandler());
                });
            });

            using var cashier = PaymentStaffClient(gateway, "cashier");
            using var nextCashier = PaymentStaffClient(gateway, "nextCashier");
            using var admin = PaymentStaffClient(gateway, "admin");
            using var waiter = PaymentStaffClient(gateway, "waiter");
            using var otherWaiter = PaymentStaffClient(gateway, "secondWaiter");
            using var sameRestaurantWaiter = PaymentStaffClient(gateway, "sameRestaurantWaiter");

            Assert.Equal(HttpStatusCode.Conflict, (await waiter.PostAsync(submit, null)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await otherWaiter.PostAsync(submit, null)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await cashier.PostAsync(submit, null)).StatusCode);
            await Read<PaymentRequestResult>(await Send(HttpMethod.Post, path + $"/{id}/acknowledge", "waiter"));
            var preview = await Read<ReviewResult>(await waiter.GetAsync(review));
            Assert.Equal(1, preview.PendingOrderCount);
            var item = Assert.Single(preview.Items);
            Assert.Equal(orderItemId, item.OrderItemId);
            Assert.Equal(location.TableId, item.TableId);
            Assert.Equal(2, item.Quantity);
            Assert.Equal("Cooking", item.Status);

            Assert.Equal(HttpStatusCode.Forbidden, (await waiter.PostAsJsonAsync(open, new { openingCash = 0 })).StatusCode);
            var openings = await Task.WhenAll(cashier.PostAsJsonAsync(open, new { openingCash = 0 }),
                cashier.PostAsJsonAsync(open, new { openingCash = 0 }));
            var opened = await Read<CashShiftDTO>(Assert.Single(openings, response => response.StatusCode == HttpStatusCode.Created),
                HttpStatusCode.Created);
            Assert.Single(openings, response => response.StatusCode == HttpStatusCode.Conflict);

            Assert.Equal(HttpStatusCode.Conflict, (await waiter.PostAsync(submit, null)).StatusCode);
            Assert.Empty((await Read<SubmissionPage>(await cashier.GetAsync(inbox))).Items);

            using (var orderingScope = ordering.Services.CreateScope())
            {
                orderingScope.ServiceProvider.GetRequiredService<Shared.Persistence.RestaurantIsolation.RestaurantDataScope>()
                    .Select(location.RestaurantId);
                var orders = orderingScope.ServiceProvider.GetRequiredService<OrderingDb>();
                var pending = await orders.Orders.SingleAsync(order => order.OrderId == pendingId);
                pending.Status = "Cancelled";
                await orders.SaveChangesAsync();
            }
            var sent = await Read<SubmissionResult>(await waiter.PostAsync(submit, null));
            Assert.Equal(id, sent.CheckoutId);
            Assert.Equal(session, sent.TableSessionId);
            Assert.Equal(opened.CashRegisterId, sent.CashRegisterId);
            Assert.Equal("Submitted", sent.Status);
            Assert.Equal(sent, Assert.Single((await Read<SubmissionPage>(await cashier.GetAsync(inbox))).Items));
            var firstCard = Assert.Single((await Read<CashierBoardPage>(await cashier.GetAsync(inbox))).Items);
            Assert.Equal(location.TableId, firstCard.TableId);
            Assert.Equal(location.AreaId, firstCard.AreaId);
            Assert.Null(firstCard.BillCode);
            Assert.Null(firstCard.TotalAmount);
            Assert.Equal(2, firstCard.PartySize);
            Assert.Equal(HttpStatusCode.Forbidden, (await nextCashier.GetAsync(inbox)).StatusCode);
            Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync("/api/cash-registers",
                new { restaurantId = location.RestaurantId, code = "ANOTHER" })).StatusCode);

            var unpaidBill = await SeedHandoverBill(billing, location, unpaidSession, paid: false);
            var paidBill = await SeedHandoverBill(billing, location, paidSession, paid: true);
            using var closed = await cashier.PostAsJsonAsync($"/api/cash-shifts/{opened.CashShiftId}/close",
                new { actualCash = 0, cashHandleOver = 0 });
            var closedReport = await Read<ShiftReportDTO>(closed, HttpStatusCode.Created);
            Assert.Equal(0, closedReport.ExpectedCash);
            Assert.Equal(HttpStatusCode.Conflict, (await nextCashier.GetAsync(inbox)).StatusCode);

            using var handoverGuest = await JoinPaymentSession(handoverSession);
            var handoverPath = PaymentRequestPath(handoverSession);
            var handoverId = Guid.NewGuid();
            await Read<PaymentRequestResult>(await PostPaymentRequest(handoverGuest, handoverPath, handoverId));
            await Read<PaymentRequestResult>(await Send(HttpMethod.Post, handoverPath + $"/{handoverId}/acknowledge", "waiter"));
            var handoverSubmit = handoverPath + $"/{handoverId}/submit";
            var parallel = await Task.WhenAll(waiter.PostAsync(handoverSubmit, null), waiter.PostAsync(handoverSubmit, null));
            var queuedDuringHandover = await Read<SubmissionResult>(parallel[0]);
            Assert.Equal(handoverId, queuedDuringHandover.CheckoutId);
            Assert.Equal(queuedDuringHandover, await Read<SubmissionResult>(parallel[1]));

            var reopened = await Read<CashShiftDTO>(await nextCashier.PostAsJsonAsync(open, new { openingCash = 0 }),
                HttpStatusCode.Created);
            Assert.Equal(opened.CashRegisterId, reopened.CashRegisterId);
            Assert.NotEqual(opened.CashierUserRef, reopened.CashierUserRef);
            Assert.Equal(HttpStatusCode.Forbidden, (await cashier.GetAsync(inbox)).StatusCode);
            var pendingItems = (await Read<SubmissionPage>(await nextCashier.GetAsync(inbox))).Items;
            Assert.Equal(3, pendingItems.Length);
            Assert.Contains(sent, pendingItems);
            Assert.Contains(queuedDuringHandover, pendingItems);
            var inherited = Assert.Single(pendingItems, value => value.BillId == unpaidBill);
            Assert.Equal(unpaidSession, inherited.TableSessionId);
            Assert.Equal("Unpaid", inherited.Status);
            Assert.Null(inherited.CheckoutId);
            Assert.DoesNotContain(pendingItems, value => value.BillId == paidBill);
            await VerifyCashierBoardFilters(nextCashier, waiter, location, handoverSession, unpaidBill, reopened.CashShiftId);
            var previousReport = await Read<ShiftReportDTO>(await cashier.GetAsync($"/api/cash-shifts/{opened.CashShiftId}/report"));
            Assert.Equivalent(closedReport, previousReport, strict: true);
            var legacy = $"/api/table-sessions/{session}/checkout/{id}";
            Assert.Equal(HttpStatusCode.Conflict, (await cashier.PostAsync(legacy + "/synchronize", null)).StatusCode);
            Assert.Equal(HttpStatusCode.Conflict, (await cashier.PostAsync(legacy + "/cancel", null)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await waiter.GetAsync(inbox)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await cashier.GetAsync(
                $"/api/restaurants/{otherRestaurant.RestaurantId}/payment-requests/incoming")).StatusCode);

            using var earlyWebhookClient = gateway.CreateClient();
            var presented = await VerifyBillPreparation(billing, ordering, location, pendingId, path + $"/{id}",
                cashier, nextCashier, waiter, otherWaiter, guest, reopened, id,
                payOsScenario == PayOsScenario.BeforePresentation ? payOs : null, earlyWebhookClient);
            if (payOsScenario == PayOsScenario.BeforePresentation)
            {
                return;
            }

            using var scope = billing.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<BillingDb>();
            scope.ServiceProvider.GetRequiredService<Shared.Persistence.RestaurantIsolation.RestaurantDataScope>().Select(location.RestaurantId);
            Assert.Equal("DEFAULT", (await db.CashRegisters.SingleAsync()).Code);
            var checkouts = await db.BillCheckouts.ToListAsync();
            Assert.Equal(2, checkouts.Count);
            Assert.All(checkouts, checkout => Assert.Equal(opened.CashRegisterId, checkout.CashRegisterId));
            Assert.Equal(3, await db.Bills.CountAsync());
            var stored = Assert.Single(checkouts, checkout => checkout.CheckoutId == id);
            Assert.Equal(presented.Bill.BillId, stored.BillId);
            Assert.Equal(reopened.CashierUserRef, stored.PreparedByRef);
            Assert.Equal(presented.PresentedAt!.Value.UtcDateTime, stored.PresentedAt);
            Assert.Empty(await db.Payments.ToListAsync());
            using var orderingGuest = gateway.CreateClient();
            await Read<ApiGateway.DTOs.Restaurant.GuestAccessDto>(await orderingGuest.PostAsJsonAsync(
                $"/api/table-sessions/{session}/guest-access/join", new { phoneOrCode = "123" }));
            await VerifyClosureRequiresPaidSession(reviewingRestaurant, billing, location, session, otherRestaurant.RestaurantId, guest);

            if (payOs is null)
            {
                await VerifyCashHandover(db, presented, reopened, nextCashier, cashier, waiter, sameRestaurantWaiter, otherWaiter, guest);
            }
            else
            {
                using var webhookClient = gateway.CreateClient();
                if (payOsScenario == PayOsScenario.Review)
                {
                    await VerifyPayOsWebhookUnderReview(db, payOs, presented, reopened, nextCashier, webhookClient, guest);
                    return;
                }

                if (payOsScenario == PayOsScenario.ConflictingWebhook)
                {
                    await VerifyConflictingPayOsWebhook(db, payOs, presented, reopened, nextCashier, webhookClient);
                    return;
                }

                if (payOsScenario == PayOsScenario.TimeoutRecovery)
                {
                    await VerifyPayOsTimeoutRecovery(billing.Services, db, payOs, presented, reopened, nextCashier,
                        waiter, orderingGuest);
                }
                else
                {
                    await VerifyPayOsPayment(db, payOs, presented, reopened, nextCashier, cashier, waiter,
                        sameRestaurantWaiter, otherWaiter, webhookClient, otherRestaurant.RestaurantId);
                }
            }

            if (recoverClosure)
            {
                await VerifyClosureAfterRecovery(reviewingRestaurant.Services, session, guest);
            }

            await VerifyClosedSession(reviewingRestaurant, location, session, id, guest, orderingGuest, waiter);
            using var checkoutScope = ordering.Services.CreateScope();
            checkoutScope.ServiceProvider.GetRequiredService<Shared.Persistence.RestaurantIsolation.RestaurantDataScope>()
                .Select(location.RestaurantId);
            var orderingDb = checkoutScope.ServiceProvider.GetRequiredService<OrderingDb>();
            var checkoutLock = await orderingDb.CheckoutLocks.AsNoTracking().SingleAsync(value => value.CheckoutId == id);
            Assert.Equal("Frozen", checkoutLock.Status);
            Assert.Null(checkoutLock.ReleasedAt);

            var billedItem = await orderingDb.OrderItems.AsNoTracking().SingleAsync(value => value.OrderItemId == orderItemId);
            var serve = $"/api/table-sessions/{session}/orders/{billedItem.OrderId}/items/{orderItemId}/serve";
            var serveBody = new { version = "" };
            Assert.Equal(HttpStatusCode.Unauthorized, (await orderingGuest.PostAsJsonAsync(serve, serveBody)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await otherWaiter.PostAsJsonAsync(serve, serveBody)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await nextCashier.PostAsJsonAsync(serve, serveBody)).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await waiter.PostAsJsonAsync(serve, serveBody)).StatusCode);

            using var kitchen = PaymentStaffClient(gateway, "paymentKitchen");
            using var becameReady = await kitchen.PatchAsJsonAsync($"/api/kitchen-tasks/{kitchenTaskId}/status",
                new { status = "Ready" });
            var ready = await Read<Ordering.Common.DTOs.KitchenTaskDTO>(becameReady);
            Assert.Equal("Ready", ready.Status);
            var ordersPath = $"/api/table-sessions/{session}/orders";
            var ordersForWaiter = await Read<List<ApiGateway.DTOs.Ordering.OrderDTO>>(await waiter.GetAsync(ordersPath));
            var readyForWaiter = Assert.Single(ordersForWaiter, value => value.OrderId == billedItem.OrderId);
            Assert.Equal("Ready", Assert.Single(readyForWaiter.Items, value => value.OrderItemId == orderItemId).Status);
            Assert.Equal(HttpStatusCode.Forbidden, (await otherWaiter.GetAsync(ordersPath)).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await orderingGuest.GetAsync(ordersPath)).StatusCode);
            var servedOrder = await Read<ApiGateway.DTOs.Ordering.OrderDTO>(await waiter.PostAsJsonAsync(serve, serveBody));
            Assert.Equal("Served", Assert.Single(servedOrder.Items, value => value.OrderItemId == orderItemId).Status);
            Assert.Equal("Served", (await orderingDb.KitchenTasks.AsNoTracking()
                .SingleAsync(value => value.KitchenTaskId == kitchenTaskId)).Status);
            Assert.Equal("Paid", (await db.Bills.AsNoTracking().SingleAsync(value => value.BillId == stored.BillId)).Status);
        }
        finally
        {
            await DeleteTestDatabase(billingConnection, "TestB2Billing_");
            await DeleteTestDatabase(orderingConnection, "TestB2Ordering_");
        }
    }

    private async Task<PreparedResult> VerifyBillPreparation(WebApplicationFactory<BillingGrpc> billing,
        WebApplicationFactory<OrderingGrpc> ordering, LocationIds location, Guid pendingOrderId, string requestPath,
        HttpClient oldCashier, HttpClient cashier, HttpClient waiter, HttpClient otherWaiter, HttpClient guest,
        CashShiftDTO shift, Guid checkoutId, PayOsHttpFixture? payOs, HttpClient webhookClient)
    {
        var prepare = requestPath + "/prepare";
        var billPath = requestPath + "/bill";
        var present = requestPath + "/present";
        Assert.Equal(HttpStatusCode.Forbidden, (await oldCashier.PostAsync(prepare, null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await waiter.PostAsync(prepare, null)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await waiter.GetAsync(billPath)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await waiter.PostAsync(present, null)).StatusCode);

        // Another order can arrive after B2. B3 verifies again under Ordering's checkout lock.
        await SetOrderStatus(ordering, location.RestaurantId, pendingOrderId, "PendingConfirmation");
        Assert.Equal(HttpStatusCode.Conflict, (await cashier.PostAsync(prepare, null)).StatusCode);
        await SetOrderStatus(ordering, location.RestaurantId, pendingOrderId, "Cancelled");
        Assert.Equal(HttpStatusCode.Conflict, (await cashier.PostAsync(prepare, null)).StatusCode);

        using (var scope = billing.Services.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<Shared.Persistence.RestaurantIsolation.RestaurantDataScope>()
                .Select(location.RestaurantId);
            var db = scope.ServiceProvider.GetRequiredService<BillingDb>();
            db.TaxFeePolicies.Add(new Billing.DAL.Models.TaxFeePolicy
            {
                TaxFeePolicyId = Guid.NewGuid(),
                Version = 1,
                VatRate = 0.1m,
                ServiceRate = 0.05m,
                ServiceEnabled = true,
                EffectiveFrom = DateTime.UtcNow.AddDays(-1)
            });
            await db.SaveChangesAsync();
        }

        var responses = await Task.WhenAll(cashier.PostAsync(prepare, null), cashier.PostAsync(prepare, null));
        var prepared = await Read<PreparedResult>(responses[0]);
        Assert.Equivalent(prepared, await Read<PreparedResult>(responses[1]), strict: true);
        Assert.Equal(checkoutId, prepared.CheckoutId);
        Assert.Equal("Prepared", prepared.CheckoutStatus);
        Assert.Equal("Unpaid", prepared.Bill.Status);
        Assert.Equal(28517m, prepared.Bill.TotalAmount);
        Assert.Equal(2, Assert.Single(prepared.Bill.Items).Quantity);
        Assert.Equal(shift.CashierUserRef, prepared.PreparedByRef);
        Assert.Null(prepared.PresentedAt);
        await AssertIncomingCheckoutStatus(cashier, location.RestaurantId, checkoutId, prepared.Bill.BillId, "Prepared");

        Assert.Equal(HttpStatusCode.Forbidden, (await otherWaiter.GetAsync(billPath)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await otherWaiter.PostAsync(present, null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await guest.GetAsync(billPath)).StatusCode);
        var viewed = await Read<PreparedResult>(await waiter.GetAsync(billPath));
        Assert.Equivalent(prepared, viewed, strict: true);
        var tooEarly = await CollectCash(waiter, prepared.Bill.BillId, checkoutId, 30000, Guid.NewGuid());
        Assert.Equal(HttpStatusCode.Conflict, tooEarly.StatusCode);
        if (payOs is not null)
        {
            return await VerifyPayOsBeforePresentation(payOs, prepared, shift, cashier, oldCashier,
                waiter, otherWaiter, webhookClient, billPath, present);
        }

        var presented = await Read<PreparedResult>(await waiter.PostAsync(present, null));
        Assert.Equal("Presented", presented.CheckoutStatus);
        Assert.Equal("Unpaid", presented.Bill.Status);
        Assert.Equal(prepared.PreparedAt, presented.PreparedAt);
        Assert.NotNull(presented.PresentedAt);
        Assert.True(presented.PresentedAt >= presented.PreparedAt);
        await AssertIncomingCheckoutStatus(cashier, location.RestaurantId, checkoutId, prepared.Bill.BillId, "Presented");
        Assert.Equivalent(presented, await Read<PreparedResult>(await waiter.PostAsync(present, null)), strict: true);
        Assert.Equivalent(presented, await Read<PreparedResult>(await cashier.PostAsync(prepare, null)), strict: true);

        using var cash = new HttpRequestMessage(HttpMethod.Post, $"/api/bills/{presented.Bill.BillId}/payments/cash")
        {
            Content = JsonContent.Create(new
            {
                checkoutId,
                cashShiftId = shift.CashShiftId,
                cashReceived = 30000
            })
        };
        cash.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        Assert.Equal(HttpStatusCode.Conflict, (await cashier.SendAsync(cash)).StatusCode);
        return presented;
    }

    private static async Task AssertIncomingCheckoutStatus(HttpClient cashier, Guid restaurantId,
        Guid checkoutId, Guid billId, string expectedStatus)
    {
        var inbox = $"/api/restaurants/{restaurantId}/payment-requests/incoming";
        var page = await Read<SubmissionPage>(await cashier.GetAsync(inbox));
        var entry = Assert.Single(page.Items, value => value.CheckoutId == checkoutId);
        Assert.Equal(billId, entry.BillId);
        Assert.Equal(expectedStatus, entry.Status);
    }

    private static async Task SetOrderStatus(WebApplicationFactory<OrderingGrpc> ordering,
        Guid restaurantId, Guid orderId, string status)
    {
        using var scope = ordering.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<Shared.Persistence.RestaurantIsolation.RestaurantDataScope>()
            .Select(restaurantId);
        var db = scope.ServiceProvider.GetRequiredService<OrderingDb>();
        var order = await db.Orders.SingleAsync(value => value.OrderId == orderId);
        order.Status = status;
        await db.SaveChangesAsync();
    }

    private async Task<Guid> PaymentSessionAtAnotherTable(LocationIds location, string code)
    {
        using var scope = _restaurant.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Restaurant.DAL.Models.RestaurantDbContext>();
        var table = new Restaurant.DAL.Models.Table
        {
            TableId = Guid.NewGuid(),
            AreaId = location.AreaId,
            TableCode = code,
            Capacity = 4,
            IsActive = true,
            CleaningStatus = "Clean"
        };
        db.Tables.Add(table);
        await db.SaveChangesAsync();
        return await SeedOpenPaymentSession(location with { TableId = table.TableId });
    }

    private async Task<Guid> SeedHandoverBill(WebApplicationFactory<BillingGrpc> billing,
        LocationIds location, Guid sessionId, bool paid)
    {
        using var scope = billing.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<Shared.Persistence.RestaurantIsolation.RestaurantDataScope>()
            .Select(location.RestaurantId);
        var db = scope.ServiceProvider.GetRequiredService<BillingDb>();
        var id = Guid.NewGuid();
        var bill = new Billing.DAL.Models.Bill
        {
            BillId = id,
            RestaurantId = location.RestaurantId,
            BillNumber = id.ToString("N"),
            Status = paid ? "Paid" : "Unpaid",
            PaidAt = paid ? _clock.Now.UtcDateTime : null,
            SubTotal = 100,
            TotalAmount = 100,
            Currency = "VND"
        };
        bill.BillLines.Add(new Billing.DAL.Models.BillLine
        {
            BillLineId = Guid.NewGuid(),
            OrderItemRef = Guid.NewGuid(),
            TableSessionRef = sessionId,
            OriginalTableRef = location.TableId,
            ItemSnapshot = "{}",
            Quantity = 1,
            UnitPriceSnapshot = 100
        });
        db.Bills.Add(bill);
        await db.SaveChangesAsync();
        return id;
    }

    private WebApplicationFactory<BillingGrpc> CreatePaymentSubmissionBillingHost(string connection,
        WebApplicationFactory<RestaurantMarker> restaurant, WebApplicationFactory<OrderingGrpc> ordering, PayOsHttpFixture? payOs,
        bool recoverClosure)
    {
        return new WebApplicationFactory<BillingGrpc>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:DefaultConnection", connection);
            builder.UseSetting("GrpcServices:Restaurant", "http://restaurant.test");
            builder.UseSetting("GrpcServices:Ordering", "http://ordering.test");
            builder.UseSetting("Notifications:ServiceKey", ClosureKey);
            if (payOs is not null)
            {
                builder.UseSetting("PayOs:RequestTimeoutSeconds", "1");
            }

            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Logging:LogLevel:Default"] = "Warning"
                }));
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IHostedService>();
                services.AddGrpcClient<BillingRestaurantAccess>()
                    .ConfigurePrimaryHttpMessageHandler(() => restaurant.Server.CreateHandler());
                services.AddGrpcClient<BillingServiceRequest>()
                    .ConfigurePrimaryHttpMessageHandler(() => restaurant.Server.CreateHandler());
                services.AddGrpcClient<BillingDisplayClient>()
                    .ConfigurePrimaryHttpMessageHandler(() => restaurant.Server.CreateHandler());
                services.AddGrpcClient<BillingClosureClient>()
                    .ConfigurePrimaryHttpMessageHandler(() => recoverClosure
                        ? new UnavailableClosureHandler() : restaurant.Server.CreateHandler());
                services.AddGrpcClient<BillingOrderClient>()
                    .ConfigurePrimaryHttpMessageHandler(() => ordering.Server.CreateHandler());
                services.AddGrpcClient<BillingCheckoutLockClient>()
                    .ConfigurePrimaryHttpMessageHandler(() => ordering.Server.CreateHandler());
                if (payOs is not null)
                {
                    services.AddHttpClient<IPayOsClient, PayOsClient>()
                        .ConfigurePrimaryHttpMessageHandler(() => payOs);
                }
            });
        });
    }

    private HttpClient PaymentStaffClient(WebApplicationFactory<ApiGateway.ApiAssembly> gateway, string role)
    {
        var client = gateway.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", _tokens[role]);
        return client;
    }

    private async Task CreatePaymentSubmissionSchema(string connection, string service)
    {
        var parsed = new SqlConnectionStringBuilder(connection);
        var name = parsed.InitialCatalog;
        var prefix = $"TestB2{service}_";
        if (!name.StartsWith(prefix, StringComparison.Ordinal) || !Guid.TryParseExact(name[prefix.Length..], "N", out _))
        {
            throw new InvalidOperationException("Only an isolated B2 database can be initialized.");
        }

        var root = _restaurant.Services.GetRequiredService<IWebHostEnvironment>().ContentRootPath;
        var path = Path.Combine(root, "..", "..", "..", "docker", "sql", "schema", $"{service}Db.sql");
        var schema = await File.ReadAllTextAsync(path);
        parsed.InitialCatalog = "master";
        await using (var master = new SqlConnection(parsed.ConnectionString))
        {
            await master.OpenAsync();
            await using var create = new SqlCommand($"CREATE DATABASE [{name}]", master);
            await create.ExecuteNonQueryAsync();
        }

        await using var database = new SqlConnection(connection);
        await database.OpenAsync();
        foreach (var batch in Regex.Split(schema, @"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase))
        {
            if (!string.IsNullOrWhiteSpace(batch))
            {
                await using var command = new SqlCommand(batch, database);
                await command.ExecuteNonQueryAsync();
            }
        }
    }

    private WebApplicationFactory<OrderingGrpc> CreatePaymentSubmissionOrderingHost(string connection)
    {
        return new WebApplicationFactory<OrderingGrpc>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:DefaultConnection", connection);
            builder.UseSetting("GrpcServices:Restaurant", "http://restaurant.test");
            builder.UseSetting("GrpcServices:Identity", "http://identity.test");
            builder.UseSetting("GrpcServices:Catalog", "http://catalog.test");
            builder.UseSetting("GrpcServices:Inventory", "http://inventory.test");
            builder.UseSetting("GrpcServices:Billing", "http://billing.test");
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IHostedService>();
                services.AddGrpcClient<OrderingRestaurantAccess>()
                    .ConfigurePrimaryHttpMessageHandler(() => _restaurant.Server.CreateHandler());
                services.AddGrpcClient<OrderingTableSession>()
                    .ConfigurePrimaryHttpMessageHandler(() => _restaurant.Server.CreateHandler());
                services.AddGrpcClient<OrderingIdentityUser>()
                    .ConfigurePrimaryHttpMessageHandler(() => _identity.Server.CreateHandler());
                services.AddGrpcClient<OrderingBillingSession>()
                    .ConfigurePrimaryHttpMessageHandler(() => _restaurant.Server.CreateHandler());
            });
        });
    }

    private async Task<Guid> SeedPaymentOrders(WebApplicationFactory<OrderingGrpc> ordering,
        LocationIds location, Guid session, Guid billedItemId, Guid kitchenTaskId)
    {
        using var scope = ordering.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<Shared.Persistence.RestaurantIsolation.RestaurantDataScope>()
            .Select(location.RestaurantId);
        var db = scope.ServiceProvider.GetRequiredService<OrderingDb>();
        var confirmed = NewOrder("Confirmed");
        var billedItem = new OrderingModels.OrderItem
        {
            OrderItemId = billedItemId,
            ItemRef = Guid.NewGuid(),
            OriginalTableRef = location.TableId,
            NameSnapshot = "Test drink",
            Quantity = 3,
            UnitPriceSnapshot = 12345,
            CancelledQuantity = 1,
            Status = "Cooking"
        };
        billedItem.KitchenTasks.Add(new OrderingModels.KitchenTask
        {
            KitchenTaskId = kitchenTaskId,
            KitchenStation = new OrderingModels.KitchenStation
            {
                KitchenStationId = Guid.NewGuid(),
                KitchenStationCode = "PREP",
                KitchenStationName = "Preparation",
                IsActive = true
            },
            BatchNumber = 1,
            Quantity = 2,
            Status = "Cooking",
            Priority = 40,
            QueuedAt = _clock.Now.UtcDateTime,
            CookingAt = _clock.Now.UtcDateTime
        });
        confirmed.OrderItems.Add(billedItem);
        confirmed.OrderItems.Add(new OrderingModels.OrderItem
        {
            OrderItemId = Guid.NewGuid(),
            ItemRef = Guid.NewGuid(),
            OriginalTableRef = location.TableId,
            NameSnapshot = "Cancelled item",
            Quantity = 1,
            CancelledQuantity = 1,
            Status = "Cancelled"
        });
        var pending = NewOrder("PendingConfirmation");
        db.Orders.AddRange(confirmed, pending);
        await db.SaveChangesAsync();
        return pending.OrderId;

        OrderingModels.Order NewOrder(string status)
        {
            var id = Guid.NewGuid();
            return new OrderingModels.Order
            {
                OrderId = id,
                OrderNumber = id.ToString("N"),
                TableSessionRef = session,
                RestaurantRef = location.RestaurantId,
                Channel = "DineIn",
                IdempotencyKey = id.ToString("N"),
                RequestHash = id.ToString("N"),
                CreatedAt = _clock.Now.UtcDateTime,
                Status = status
            };
        }
    }

    private enum PayOsScenario
    {
        Settlement,
        BeforePresentation,
        Review,
        ConflictingWebhook,
        TimeoutRecovery
    }

    private sealed record PreparedResult(Guid CheckoutId, Guid TableSessionId, string CheckoutStatus, BillDTO Bill,
        Guid PreparedByRef, DateTimeOffset PreparedAt, DateTimeOffset? PresentedAt);
    private sealed record ReviewResult(int PendingOrderCount, ReviewItem[] Items);
    private sealed record ReviewItem(Guid OrderItemId, Guid TableId, int Quantity, string Status);
    private sealed record SubmissionPage(SubmissionResult[] Items);
    private sealed record SubmissionResult(Guid? CheckoutId, Guid TableSessionId, Guid CashRegisterId,
        DateTimeOffset? SubmittedAt, string Status, Guid? BillId);
}
