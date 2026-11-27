using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using ApiGateway.DTOs.Billing;
using Microsoft.EntityFrameworkCore;
using Shared.Common.Pagination;
using Xunit;
using BillingDb = Billing.DAL.Models.BillingDbContext;

namespace Restaurant.Tests;

public sealed partial class ReservationSqlServerTests
{
    private static async Task VerifyCashHandover(BillingDb db, PreparedResult presented, CashShiftDTO shift,
        HttpClient cashier, HttpClient oldCashier, HttpClient waiter, HttpClient otherWaiter,
        HttpClient crossRestaurantWaiter, HttpClient guest)
    {
        const decimal received = 30000m;
        var billId = presented.Bill.BillId;
        var checkoutId = presented.CheckoutId;
        var key = Guid.NewGuid();
        var queue = $"/api/cash-registers/{shift.CashRegisterId}/cash-handovers";
        var reconcile = $"/api/cash-shifts/{shift.CashShiftId}/reconciliation";
        var close = $"/api/cash-shifts/{shift.CashShiftId}/close";
        using var swagger = JsonDocument.Parse(await cashier.GetStringAsync("/swagger/v1/swagger.json"));
        var paths = swagger.RootElement.GetProperty("paths");
        Assert.True(paths.GetProperty("/api/bills/{billId}/cash-handovers").GetProperty("post")
            .GetProperty("requestBody").GetProperty("required").GetBoolean());
        Assert.True(paths.GetProperty("/api/cash-handovers/{paymentId}/confirm").GetProperty("post")
            .GetProperty("requestBody").GetProperty("required").GetBoolean());
        Assert.True(paths.GetProperty("/api/cash-handovers/{paymentId}").TryGetProperty("get", out _));
        Assert.True(paths.GetProperty("/api/cash-registers/{cashRegisterId}/cash-handovers").TryGetProperty("get", out _));
        await AssertCashHandoverRequiresBody(waiter, $"/api/bills/{billId}/cash-handovers", key);
        Assert.Equal(HttpStatusCode.Unauthorized, (await CollectCash(guest, billId, checkoutId, received, key)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await CollectCash(cashier, billId, checkoutId, received, key)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await CollectCash(otherWaiter, billId, checkoutId, received, key)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await CollectCash(crossRestaurantWaiter, billId, checkoutId, received, key)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await CollectCash(waiter, billId, checkoutId, 25000, key)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await CollectCash(waiter, billId, checkoutId, 30000.50m, key)).StatusCode);
        var cashMethod = await db.PaymentMethods.SingleAsync(value => value.MethodCode == "Cash");
        cashMethod.IsEnabled = false;
        await db.SaveChangesAsync();
        Assert.Equal(HttpStatusCode.Conflict, (await CollectCash(waiter, billId, checkoutId, received, key)).StatusCode);
        Assert.False(await db.Payments.AsNoTracking().AnyAsync(value => value.MethodCode == "Cash"));

        cashMethod.IsEnabled = true;
        await db.SaveChangesAsync();

        var otherKey = Guid.NewGuid();
        var simultaneous = await Task.WhenAll(
            CollectCash(waiter, billId, checkoutId, 30000.00m, key),
            CollectCash(waiter, billId, checkoutId, received, key),
            CollectCash(waiter, billId, checkoutId, received, otherKey));
        // A request with another key may win the lock before the same-key pair.
        var created = Assert.Single(simultaneous, response => response.StatusCode == HttpStatusCode.Created);
        var collected = await Read<CashHandoverDTO>(created, HttpStatusCode.Created);
        var acceptedKey = simultaneous[2] == created ? otherKey : key;
        if (simultaneous[2] == created)
        {
            Assert.All(simultaneous.Take(2), value => Assert.Equal(HttpStatusCode.Conflict, value.StatusCode));
        }
        else
        {
            var repeated = Assert.Single(simultaneous, value => value.StatusCode == HttpStatusCode.OK);
            Assert.Equivalent(collected, await Read<CashHandoverDTO>(repeated), strict: true);
            Assert.Equal(HttpStatusCode.Conflict, simultaneous[2].StatusCode);
        }

        Assert.Equal("Pending", collected.Status);
        Assert.Equal("PendingHandover", collected.PaymentStatus);
        Assert.Equal(28517m, collected.Amount);
        Assert.Equal(1483m, collected.ChangeAmount);
        Assert.Equal(shift.CashShiftId, collected.CashShiftId);
        Assert.Equal(collected.RestaurantId, await db.CashHandovers.AsNoTracking()
            .Where(value => value.PaymentId == collected.PaymentId)
            .Select(value => EF.Property<Guid?>(value, "RestaurantId")).SingleAsync());
        Assert.Equivalent(collected, await Read<CashHandoverDTO>(
            await CollectCash(waiter, billId, checkoutId, received, acceptedKey)), strict: true);
        using var bypass = new HttpRequestMessage(HttpMethod.Post, $"/api/bills/{billId}/payments/cash")
        {
            Content = JsonContent.Create(new { checkoutId, cashShiftId = shift.CashShiftId, cashReceived = received })
        };
        bypass.Headers.Add("Idempotency-Key", acceptedKey.ToString());
        Assert.Equal(HttpStatusCode.Conflict, (await cashier.SendAsync(bypass)).StatusCode);
        Assert.Null(collected.CashierUserRef);
        Assert.Null(collected.ConfirmedAt);
        Assert.Null(collected.ConfirmedAmount);
        var paymentPath = $"/api/cash-handovers/{collected.PaymentId}";
        Assert.Equivalent(collected, await Read<CashHandoverDTO>(await waiter.GetAsync(paymentPath)), strict: true);
        Assert.Equal(HttpStatusCode.Forbidden, (await otherWaiter.GetAsync(paymentPath)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await oldCashier.GetAsync(queue)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await waiter.GetAsync(queue)).StatusCode);
        var pending = await Read<PagedResult<CashHandoverDTO>>(await cashier.GetAsync(queue));
        Assert.Equal(collected.PaymentId, Assert.Single(pending.Items).PaymentId);

        Assert.Equal("Unpaid", (await db.Bills.AsNoTracking().SingleAsync(bill => bill.BillId == billId)).Status);
        Assert.Empty((await Read<CashierBoardPage>(await cashier.GetAsync(
            $"/api/restaurants/{shift.RestaurantId}/payment-requests/paid"))).Items);
        Assert.Contains((await Read<CashierBoardPage>(await cashier.GetAsync(
            $"/api/restaurants/{shift.RestaurantId}/payment-requests/incoming"))).Items, card => card.BillId == billId);
        Assert.Equal("Presented", (await db.BillCheckouts.AsNoTracking().SingleAsync(value => value.CheckoutId == checkoutId)).Status);
        Assert.Empty(await db.CashMovements.AsNoTracking().ToListAsync());
        var before = await Read<ShiftReconciliationDTO>(await cashier.GetAsync(reconcile));
        Assert.False(before.CanClose);
        Assert.Equal(shift.OpeningCash, before.ExpectedCash);
        Assert.Equal(0, before.SaleTotal);
        var blocker = Assert.Single(before.Blockers, value => value.Code == "UnresolvedPayment");
        Assert.Equal(collected.PaymentId, blocker.PaymentId);
        Assert.Equal("UnresolvedPayment", blocker.Code);
        Assert.Contains("chưa xác nhận bàn giao", blocker.Message);
        Assert.Equal(HttpStatusCode.Conflict, (await cashier.PostAsJsonAsync(close, new { actualCash = 0, cashHandleOver = 0 })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await cashier.GetAsync($"/api/cash-shifts/{shift.CashShiftId}/report")).StatusCode);

        var confirm = paymentPath + "/confirm";
        await AssertCashHandoverRequiresBody(cashier, confirm);
        Assert.Equal(HttpStatusCode.Forbidden, (await waiter.PostAsJsonAsync(confirm, new { confirmedAmount = received })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await oldCashier.PostAsJsonAsync(confirm, new { confirmedAmount = received })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await cashier.PostAsJsonAsync(confirm, new { confirmedAmount = 28517 })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await cashier.PostAsJsonAsync(confirm, new { confirmedAmount = 31000 })).StatusCode);
        Assert.Equal("PendingHandover", (await db.Payments.AsNoTracking().SingleAsync(value => value.MethodCode == "Cash")).Status);
        Assert.Empty(await db.CashMovements.AsNoTracking().ToListAsync());
        Assert.Equal(HttpStatusCode.OK, (await guest.GetAsync(
            $"/api/table-sessions/{presented.TableSessionId}/guest-access/me")).StatusCode);

        var confirmations = await Task.WhenAll(
            cashier.PostAsJsonAsync(confirm, new { confirmedAmount = received }),
            cashier.PostAsJsonAsync(confirm, new { confirmedAmount = received }));
        var confirmed = await Read<CashHandoverDTO>(confirmations[0]);
        Assert.Equivalent(confirmed, await Read<CashHandoverDTO>(confirmations[1]), strict: true);
        Assert.Equal(collected.PaymentId, confirmed.PaymentId);
        Assert.Equal("Confirmed", confirmed.Status);
        Assert.Equal("Paid", confirmed.PaymentStatus);
        Assert.Equal(received, confirmed.ConfirmedAmount);
        Assert.Equal(1483m, confirmed.ChangeAmount);
        Assert.Equal(shift.CashierUserRef, confirmed.CashierUserRef);
        Assert.NotNull(confirmed.ConfirmedAt);
        Assert.Equivalent(confirmed, await Read<CashHandoverDTO>(await waiter.GetAsync(paymentPath)), strict: true);
        Assert.Empty((await Read<PagedResult<CashHandoverDTO>>(await cashier.GetAsync(queue))).Items);

        var payment = await db.Payments.AsNoTracking().SingleAsync(value => value.MethodCode == "Cash");
        Assert.Equal(collected.PaymentId, payment.PaymentId);
        Assert.Equal("Paid", payment.Status);
        Assert.Equal("Cashier", payment.VerificationSource);
        var sale = await db.CashMovements.AsNoTracking().SingleAsync();
        Assert.Equal(payment.PaymentId, sale.PaymentId);
        Assert.Equal("Sale", sale.Type);
        Assert.Equal(28517, sale.SignedAmount);
        Assert.Equal("Paid", (await db.Bills.AsNoTracking().SingleAsync(bill => bill.BillId == billId)).Status);
        Assert.Equal("Paid", (await db.BillCheckouts.AsNoTracking().SingleAsync(value => value.CheckoutId == checkoutId)).Status);
        await AssertPaidCashierBoard(cashier, shift.RestaurantId, shift.CashShiftId, billId, presented.Bill.TotalAmount);
        var after = await Read<ShiftReconciliationDTO>(await cashier.GetAsync(reconcile));
        Assert.True(after.CanClose);
        Assert.Equal(28517m, after.SaleTotal);
        Assert.Equal(shift.OpeningCash + 28517m, after.ExpectedCash);
        Assert.Equal(0, after.BankTransferTotal);
        var report = await Read<ShiftReportDTO>(await cashier.PostAsJsonAsync(close, new { actualCash = 28517, cashHandleOver = 0 }),
            HttpStatusCode.Created);
        Assert.Equal(after.ExpectedCash, report.ExpectedCash);
        Assert.Equal(0, report.Difference);
        Assert.Equal(report.ShiftReportId, (await Read<ShiftReportDTO>(await cashier.PostAsJsonAsync(close,
            new { actualCash = 28517, cashHandleOver = 0 }))).ShiftReportId);
        Assert.Equivalent(confirmed, await Read<CashHandoverDTO>(await cashier.PostAsJsonAsync(confirm,
            new { confirmedAmount = received })), strict: true);
    }

    private static async Task AssertCashHandoverRequiresBody(HttpClient client, string path, Guid? key = null)
    {
        HttpContent?[] missingBodies =
        {
            null,
            new ByteArrayContent([]),
            new StringContent(string.Empty, Encoding.UTF8, "application/json"),
            new StringContent("null", Encoding.UTF8, "application/json")
        };
        foreach (var content in missingBodies)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, path)
            {
                Content = content
            };
            if (key.HasValue)
            {
                request.Headers.Add("Idempotency-Key", key.Value.ToString());
            }

            using var response = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
    }

    private static async Task<HttpResponseMessage> CollectCash(HttpClient client, Guid billId, Guid checkoutId, decimal amount, Guid key)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/bills/{billId}/cash-handovers")
        {
            Content = JsonContent.Create(new { checkoutId, cashReceived = amount })
        };
        request.Headers.Add("Idempotency-Key", key.ToString());
        return await client.SendAsync(request);
    }
}
