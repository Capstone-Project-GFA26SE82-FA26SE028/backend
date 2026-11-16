using Billing.BLL.Implements;
using Billing.Common.DTOs;
using Billing.Common.Exceptions;
using Billing.DAL.Implements;
using Billing.DAL.Models;
using Microsoft.EntityFrameworkCore;
using Shared.Persistence.UnitOfWorks;

namespace Billing.Tests;

public sealed class OnlinePaymentTests
{
    [Fact]
    public async Task PaidStatusRequiresVerifiedExactAmountAndRepeatedWebhookSettlesOnce()
    {
        await using var fixture = await PayOsPaymentServiceTests.Fixture.Create();
        var db = fixture.Core.Db;
        var service = new OnlinePaymentService(new OnlinePaymentRepository(db), new PaymentRepository(db),
            new UnitOfWork<BillingDbContext>(db), fixture.Provider, fixture.Core.Core.Actor, TimeProvider.System);
        var request = new OnlinePaymentCommand(Guid.NewGuid(), fixture.Core.Bill.RestaurantId, Guid.NewGuid(), 100000, "Create");
        var pending = await service.Execute(request, default);
        Assert.Equal("Pending", pending.Status);
        await service.Execute(request, default);
        Assert.Equal(1, fixture.Provider.CreateCalls);
        Assert.Single(await db.OnlinePayments.ToListAsync());
        fixture.Provider.Settle(90000);
        await Assert.ThrowsAsync<BillingException>(() => service.Execute(request with { Action = "Refresh" }, default));
        Assert.Equal("Pending", (await db.OnlinePayments.AsNoTracking().SingleAsync()).Status);
        fixture.Provider.Settle(100000);
        fixture.Provider.Webhook = fixture.Event(100000);
        Assert.True(await service.ProcessWebhook("verified", default));
        var paidAt = (await db.OnlinePayments.AsNoTracking().SingleAsync()).PaidAt;
        Assert.NotNull(paidAt);
        Assert.True(await service.ProcessWebhook("verified", default));
        Assert.Equal(paidAt, (await db.OnlinePayments.AsNoTracking().SingleAsync()).PaidAt);
    }

    [Fact]
    public async Task CancellationRaceBecomesPendingRefundAndOnlyCashierEvidenceCompletesIt()
    {
        await using var fixture = await PayOsPaymentServiceTests.Fixture.Create();
        var db = fixture.Core.Db;
        var actor = fixture.Core.Core.Actor;
        var service = new OnlinePaymentService(new OnlinePaymentRepository(db), new PaymentRepository(db),
            new UnitOfWork<BillingDbContext>(db), fixture.Provider, actor, TimeProvider.System);
        var request = new OnlinePaymentCommand(Guid.NewGuid(), fixture.Core.Bill.RestaurantId, Guid.NewGuid(), 50000, "Create");
        await service.Execute(request, default);
        fixture.Provider.Settle(50000);
        var refund = await service.Execute(request with { Action = "Cancel", Reason = "Customer cancellation" }, default);
        Assert.Equal("RefundPending", refund.Status);
        Assert.Null(refund.RefundedAt);
        var verify = request with
        {
            Action = "VerifyRefund",
            Reference = "BANK-REFUND-1",
            Evidence = "https://bank.example/evidence/1"
        };
        actor.Scope.RoleId = "waiter";
        Assert.Equal(403, (await Assert.ThrowsAsync<BillingException>(() => service.Execute(verify, default))).StatusCode);
        actor.Scope.RoleId = "cashier";
        Assert.Equal(400, (await Assert.ThrowsAsync<BillingException>(() => service.Execute(verify with { Evidence = "" }, default))).StatusCode);
        var result = await service.Execute(verify, default);
        Assert.Equal("Refunded", result.Status);
        Assert.NotNull(result.RefundedAt);
        var repeated = await service.Execute(verify, default);
        Assert.Equal(result.RefundedAt, repeated.RefundedAt);
        var row = await db.OnlinePayments.SingleAsync();
        Assert.Equal(actor.Scope.UserId, row.RefundVerifiedBy);
        Assert.Equal(verify.Evidence, row.RefundEvidence);
    }

    [Fact]
    public async Task CancelBeforeDelayedCreatePersistsTombstoneAndNeverCreatesProviderLink()
    {
        await using var fixture = await PayOsPaymentServiceTests.Fixture.Create();
        var db = fixture.Core.Db;
        var service = new OnlinePaymentService(new OnlinePaymentRepository(db), new PaymentRepository(db),
            new UnitOfWork<BillingDbContext>(db), fixture.Provider, fixture.Core.Core.Actor, TimeProvider.System);
        var request = new OnlinePaymentCommand(Guid.NewGuid(), fixture.Core.Bill.RestaurantId, Guid.NewGuid(), 100000, "Cancel");
        Assert.Equal("Cancelled", (await service.Execute(request, default)).Status);
        Assert.Equal("Cancelled", (await service.Execute(request with { Action = "Create" }, default)).Status);
        Assert.Equal(0, fixture.Provider.CreateCalls);
        Assert.Single(await db.OnlinePayments.ToListAsync());
        await Assert.ThrowsAsync<BillingException>(() => service.Execute(request with { Action = "Create", Amount = 1 }, default));
    }

    [Fact]
    public async Task ProviderTimeoutKeepsStableOrderCodeAndCannotMarkPaidOrCancelled()
    {
        await using var fixture = await PayOsPaymentServiceTests.Fixture.Create();
        var db = fixture.Core.Db;
        var service = new OnlinePaymentService(new OnlinePaymentRepository(db), new PaymentRepository(db),
            new UnitOfWork<BillingDbContext>(db), fixture.Provider, fixture.Core.Core.Actor, TimeProvider.System);
        var request = new OnlinePaymentCommand(Guid.NewGuid(), fixture.Core.Bill.RestaurantId, Guid.NewGuid(), 100000, "Create");
        fixture.Provider.Offline = true;
        await Assert.ThrowsAsync<PayOsClientException>(() => service.Execute(request, default));
        var code = (await db.OnlinePayments.AsNoTracking().SingleAsync()).ProviderOrderCode;
        fixture.Provider.Offline = false;
        await service.Execute(request, default);
        Assert.Equal(code, fixture.Provider.Value!.OrderCode);
        fixture.Provider.CancelTimeout = true;
        await Assert.ThrowsAsync<PayOsClientException>(() => service.Execute(request with { Action = "Cancel" }, default));
        Assert.Equal("Pending", (await db.OnlinePayments.AsNoTracking().SingleAsync()).Status);
    }
}
