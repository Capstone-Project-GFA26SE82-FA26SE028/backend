using Microsoft.EntityFrameworkCore;

namespace Billing.Tests;

public sealed class PaymentSettlementRecoveryTests
{
    [Fact]
    public async Task RestaurantFailureAfterCashConfirmationKeepsPaidAndReplayDoesNotCollectTwice()
    {
        await using var f = await CashPaymentServiceTests.Fixture.Create();
        await f.Collect(300);
        var client = new TestSessionSettlementClient
        {
            Throw = true
        };
        var service = CashPaymentServiceTests.Fixture.MakeService(f.Db, f.Core.Actor, f.Core.Service, f.Remote, client);
        var result = await service.PayCash(f.Bill.BillId, f.Key, f.Body(300), default);
        Assert.False(result.Payment.SessionClosureConfirmed);
        Assert.Equal("Paid", result.Payment.Status);
        Assert.Equal("Confirmed", (await f.Db.CashHandovers.SingleAsync()).Status);
        Assert.Equal("Paid", (await f.Db.Bills.SingleAsync()).Status);
        Assert.Equal("Paid", (await f.Db.BillCheckouts.SingleAsync()).Status);
        Assert.Null((await f.Db.BillCheckouts.SingleAsync()).ReleasedAt);
        client.Throw = false;
        var repeated = await service.PayCash(f.Bill.BillId, f.Key, f.Body(300), default);
        Assert.True(repeated.Payment.SessionClosureConfirmed);
        Assert.False(repeated.Created);
        Assert.Equal(result.Payment.PaymentId, repeated.Payment.PaymentId);
        Assert.Equal(result.Payment.PaidAt, repeated.Payment.PaidAt);
        Assert.Single(await f.Db.Payments.ToListAsync());
        Assert.Single(await f.Db.CashMovements.ToListAsync());
        Assert.Equal(2, client.Calls.Count);
        Assert.All(client.Calls, payment =>
        {
            Assert.Equal(f.CheckoutId, payment.CheckoutId);
            Assert.Equal(f.Bill.BillId, payment.BillId);
            Assert.Equal(f.Core.Source.Value.TableSessionId, payment.TableSessionId);
        });
    }

    [Fact]
    public async Task PendingCollectionNeverProducesPaidProofOrClosureCall()
    {
        await using var f = await CashPaymentServiceTests.Fixture.Create();
        await f.Collect(300);
        var client = new TestSessionSettlementClient();
        var settlement = TestPaymentSettlement.Create(f.Db, client);
        Assert.False(await settlement.TryClose(f.Core.Source.Value.TableSessionId, f.Bill.RestaurantId, default));
        Assert.Empty(client.Calls);
        Assert.Equal("Unpaid", (await f.Db.Bills.SingleAsync()).Status);
        Assert.Empty(await f.Db.CashMovements.ToListAsync());
    }
}
