using Billing.Common.DTOs;
using Billing.Common.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace Billing.Tests;

public sealed partial class PayOsSqlServerTests
{
    [BillingSqlServerFact]
    public async Task WaiterPageFiltersExactCheckoutOnSqlServerWithoutCallingBankOrChangingPayment()
    {
        var source = new BillServiceTests.BillSource();
        var actor = CashActor(source);
        var prepared = await PrepareCash(source, actor);
        await using var db = Context();
        var provider = new PayOsPaymentServiceTests.FakeProvider();
        var waiter = Guid.NewGuid();
        var service = Make(db, source, actor, provider, waiter);
        var created = await service.Create(prepared.BillId, Guid.NewGuid().ToString(), new CreatePayOsPaymentDTO
        {
            CheckoutId = prepared.Body.CheckoutId,
            CashShiftId = prepared.Body.CashShiftId
        }, default);
        var calls = provider.GetCalls;
        actor.Scope.UserId = waiter;
        actor.Scope.RoleId = "waiter";
        var page = await service.GetForCheckout(source.Value.TableSessionId, prepared.Body.CheckoutId, 1, 1, default);
        var value = Assert.Single(page.Items);
        Assert.Equal(created.Payment.PaymentId, value.PaymentId);
        Assert.Equal(1, page.TotalCount);
        Assert.True(value.CanPresentQr);
        Assert.Equal(232, value.Amount);
        Assert.Equal(calls, provider.GetCalls);
        Assert.False(db.ChangeTracker.HasChanges());
        var payment = await db.Payments.SingleAsync();
        payment.IdempotencyKey = "PayOS:" + Guid.NewGuid().ToString("N") + ":" + Guid.NewGuid().ToString("N");
        await db.SaveChangesAsync();
        Assert.Empty((await service.GetForCheckout(source.Value.TableSessionId, prepared.Body.CheckoutId, 1, 20, default)).Items);
        Assert.Equal(404, (await Assert.ThrowsAsync<BillingException>(() => service.GetForWaiter(
            source.Value.TableSessionId, prepared.Body.CheckoutId, payment.PaymentId, default))).StatusCode);
        Assert.Single(await db.Payments.ToListAsync());
        Assert.Empty(await db.CashMovements.ToListAsync());
        Assert.Equal("Unpaid", (await db.Bills.SingleAsync()).Status);
    }

    [BillingSqlServerFact]
    public async Task WaiterReadWaitsForSessionUpdateAndDoesNotReturnCancelledQr()
    {
        var source = new BillServiceTests.BillSource();
        var actor = CashActor(source);
        var prepared = await PrepareCash(source, actor);
        var provider = new PayOsPaymentServiceTests.FakeProvider();
        var waiterId = Guid.NewGuid();
        Guid paymentId;
        await using (var setup = Context())
        {
            paymentId = (await Make(setup, source, actor, provider).Create(prepared.BillId, Guid.NewGuid().ToString(),
                new CreatePayOsPaymentDTO
                {
                    CheckoutId = prepared.Body.CheckoutId,
                    CashShiftId = prepared.Body.CashShiftId
                }, default)).Payment.PaymentId;
        }

        await using var writer = Context();
        await using var reader = Context();
        await using var transaction = await writer.Database.BeginTransactionAsync();
        Assert.True(await new Billing.DAL.Implements.BillRepository(writer).LockSessionAsync(source.Value.TableSessionId, default));
        actor.Scope.RoleId = "waiter";
        actor.Scope.UserId = waiterId;
        var pending = Make(reader, source, actor, provider, waiterId).GetForWaiter(
            source.Value.TableSessionId, prepared.Body.CheckoutId, paymentId, default);
        await Task.Delay(250);
        Assert.False(pending.IsCompleted);
        var value = await writer.Payments.SingleAsync();
        value.Status = "Cancelled";
        value.VerificationSource = "PayOSZero";
        await writer.SaveChangesAsync();
        await transaction.CommitAsync();
        var result = await pending.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal("Cancelled", result.Status);
        Assert.False(result.CanPresentQr);
        Assert.Null(result.QrCode);
        Assert.Null(result.CheckoutUrl);
    }
}
