using Billing.Common.DTOs;
using Billing.Common.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace Billing.Tests;

public sealed class CashHandoverServiceTests
{
    [Fact]
    public async Task WaiterCollectionStaysPendingUntilCashierReceivesWholeTenderAndGivesChange()
    {
        await using var f = await CashPaymentServiceTests.Fixture.Create();
        var collected = await f.Collect(300);
        Assert.True(collected.Created);
        Assert.Equal("Pending", collected.Handover.Status);
        Assert.Equal("PendingHandover", collected.Handover.PaymentStatus);
        Assert.Null(collected.Handover.CashierUserRef);
        Assert.Null(collected.Handover.ConfirmedAt);
        Assert.Equal(232, collected.Handover.Amount);
        Assert.Equal(300, collected.Handover.CashReceived);
        Assert.Equal(68, collected.Handover.ChangeAmount);
        Assert.Equal("Unpaid", (await f.Db.Bills.SingleAsync()).Status);
        Assert.Empty(await f.Db.CashMovements.ToListAsync());
        var cashier = f.Handovers(f.Core.Actor);
        Assert.Equal(collected.Handover.PaymentId, Assert.Single((await cashier.GetAll(
            collected.Handover.CashRegisterId, 1, 20, default)).Items).PaymentId);
        Assert.False((await ShiftReportServiceTests.Make(f.Db, f.Core.Actor).Preview(f.ShiftId, default)).CanClose);
        var confirmed = await cashier.Confirm(collected.Handover.PaymentId, 300, default);
        Assert.Equal("Confirmed", confirmed.Status);
        Assert.Equal("Paid", confirmed.PaymentStatus);
        Assert.Equal(300, confirmed.ConfirmedAmount);
        Assert.Equal(f.Core.Actor.Scope.UserId, confirmed.CashierUserRef);
        Assert.NotNull(confirmed.ConfirmedAt);
        Assert.Equal(232, (await f.Db.CashMovements.SingleAsync()).SignedAmount);
        Assert.Equal("Paid", (await f.Db.Bills.SingleAsync()).Status);
        Assert.Equal("Paid", (await f.Db.BillCheckouts.SingleAsync()).Status);
        Assert.Null((await f.Db.BillCheckouts.SingleAsync()).ReleasedAt);
        var waiter = CheckoutRoutingTests.Waiter(f.Core.Source, f.WaiterId);
        Assert.Equal("Confirmed", (await f.Handovers(waiter).GetById(confirmed.PaymentId, default)).Status);
        Assert.Equal(confirmed.ConfirmedAt, (await cashier.Confirm(confirmed.PaymentId, 300, default)).ConfirmedAt);
        Assert.False((await f.Collect(300)).Created);
        Assert.True((await ShiftReportServiceTests.Make(f.Db, f.Core.Actor).Preview(f.ShiftId, default)).CanClose);
        Assert.Single(await f.Db.Payments.ToListAsync());
        Assert.Single(await f.Db.CashHandovers.ToListAsync());
        Assert.Single(await f.Db.CashMovements.ToListAsync());
    }

    [Fact]
    public async Task DirectCashierRouteCannotSkipWaiterCollection()
    {
        await using var f = await CashPaymentServiceTests.Fixture.Create();
        await Error(409, () => f.Service.PayCash(f.Bill.BillId, f.Key, f.Body(300), default));
        Assert.Empty(await f.Db.Payments.ToListAsync());
        Assert.Empty(await f.Db.CashHandovers.ToListAsync());
        Assert.Empty(await f.Db.CashMovements.ToListAsync());
    }

    [Theory]
    [InlineData("cashier")]
    [InlineData("manager")]
    [InlineData("admin")]
    public async Task OnlyResponsibleWaiterCanRecordCollection(string role)
    {
        await using var f = await CashPaymentServiceTests.Fixture.Create();
        var actor = CheckoutRoutingTests.Waiter(f.Core.Source, f.WaiterId);
        actor.Scope.RoleId = role;
        actor.Scope.IsAdministrator = role == "admin";
        await Error(403, () => f.Handovers(actor).Create(f.Bill.BillId, f.Key, new CreateCashHandoverDTO
        {
            CheckoutId = f.CheckoutId,
            CashReceived = 300
        }, default));
        Assert.Empty(await f.Db.Payments.ToListAsync());
    }

    [Fact]
    public async Task OtherWaiterAndCrossRestaurantCannotCreateOrReadCollection()
    {
        await using var f = await CashPaymentServiceTests.Fixture.Create();
        var actor = CheckoutRoutingTests.Waiter(f.Core.Source, Guid.NewGuid());
        var service = f.Handovers(actor);
        await Error(403, () => service.Create(f.Bill.BillId, f.Key, new CreateCashHandoverDTO
        {
            CheckoutId = f.CheckoutId,
            CashReceived = 300
        }, default));
        var saved = await f.Collect(300);
        await Error(403, () => service.GetById(saved.Handover.PaymentId, default));
        actor.Scope.UserId = f.WaiterId;
        actor.Scope.RestaurantIds = [Guid.NewGuid()];
        await Error(403, () => service.GetById(saved.Handover.PaymentId, default));
    }

    [Fact]
    public async Task WrongCashierAndWrongActualAmountCannotSettleOrCreateSale()
    {
        await using var f = await CashPaymentServiceTests.Fixture.Create();
        var saved = await f.Collect(300);
        var cashier = f.Handovers(f.Core.Actor);
        await Error(409, () => cashier.Confirm(saved.Handover.PaymentId, 299, default));
        var wrong = CheckoutRoutingTests.Waiter(f.Core.Source, Guid.NewGuid());
        wrong.Scope.RoleId = "cashier";
        await Error(403, () => f.Handovers(wrong).Confirm(saved.Handover.PaymentId, 300, default));
        var waiter = CheckoutRoutingTests.Waiter(f.Core.Source, f.WaiterId);
        await Error(403, () => f.Handovers(waiter).Confirm(saved.Handover.PaymentId, 300, default));
        Assert.Equal("PendingHandover", (await f.Db.Payments.SingleAsync()).Status);
        Assert.Null((await f.Db.CashHandovers.SingleAsync()).ConfirmedAt);
        Assert.Empty(await f.Db.CashMovements.ToListAsync());
        Assert.Equal("Unpaid", (await f.Db.Bills.SingleAsync()).Status);
    }

    [Fact]
    public async Task RetryUsesOriginalCollectionAndNewKeyCannotCollectSameBillAgain()
    {
        await using var f = await CashPaymentServiceTests.Fixture.Create();
        var first = await f.Collect(300);
        var retry = await f.Collect(300);
        Assert.False(retry.Created);
        Assert.Equal(first.Handover.PaymentId, retry.Handover.PaymentId);
        Assert.Equal(first.Handover.CollectedAt, retry.Handover.CollectedAt);
        await Error(409, () => f.Collect(301));
        await Error(409, () => f.Collect(300, Guid.NewGuid().ToString()));
        Assert.Single(await f.Db.CashHandovers.ToListAsync());
    }

    [Fact]
    public async Task ConfirmationWriteFailurePreservesPendingMoneyAndRetrySettlesExactlyOnce()
    {
        await using var f = await CashPaymentServiceTests.Fixture.Create();
        var first = await f.Collect(300);
        var service = f.Handovers(f.Core.Actor);
        await f.Db.Database.ExecuteSqlRawAsync("CREATE TRIGGER FailSale BEFORE INSERT ON CashMovement BEGIN SELECT RAISE(ABORT, 'test write failure'); END;");
        await Assert.ThrowsAsync<DbUpdateException>(() => service.Confirm(first.Handover.PaymentId, 300, default));
        f.Db.ChangeTracker.Clear();
        Assert.Equal("PendingHandover", (await f.Db.Payments.SingleAsync()).Status);
        Assert.Equal("Pending", (await f.Db.CashHandovers.SingleAsync()).Status);
        Assert.Null((await f.Db.Payments.SingleAsync()).PaidAt);
        Assert.Equal("Unpaid", (await f.Db.Bills.SingleAsync()).Status);
        Assert.Empty(await f.Db.CashMovements.ToListAsync());
        await f.Db.Database.ExecuteSqlRawAsync("DROP TRIGGER FailSale;");
        Assert.Equal("Paid", (await service.Confirm(first.Handover.PaymentId, 300, default)).PaymentStatus);
        Assert.Single(await f.Db.Payments.ToListAsync());
        Assert.Single(await f.Db.CashMovements.ToListAsync());
    }

    [Fact]
    public async Task PendingHandoverBlocksShiftClosing()
    {
        await using var f = await CashPaymentServiceTests.Fixture.Create();
        await f.Collect(300);
        var checkout = (await f.Db.BillCheckouts.SingleAsync());
        Assert.Equal("Ready", checkout.Status);
        Assert.Null(checkout.ReleasedAt);
        Assert.Contains((await ShiftReportServiceTests.Make(f.Db, f.Core.Actor).Preview(f.ShiftId, default)).Blockers,
            blocker => blocker.Code == "UnresolvedPayment");
    }

    [Fact]
    public async Task LegacyPaidReceiptWithoutHandoverStillReplaysAfterShiftClosed()
    {
        await using var f = await CashPaymentServiceTests.Fixture.Create();
        var paid = await f.Pay(300);
        f.Db.CashHandovers.RemoveRange(f.Db.CashHandovers);
        (await f.Db.CashShifts.SingleAsync()).ClosedAt = DateTime.UtcNow;
        (await f.Db.PaymentMethods.SingleAsync()).IsEnabled = false;
        await f.Db.SaveChangesAsync();
        var replay = await f.Service.PayCash(f.Bill.BillId, f.Key, f.Body(300), default);
        Assert.False(replay.Created);
        Assert.Equal(paid.Payment.PaymentId, replay.Payment.PaymentId);
        Assert.Single(await f.Db.Payments.ToListAsync());
        Assert.Single(await f.Db.CashMovements.ToListAsync());
    }

    private static async Task Error(int status, Func<Task> action)
    {
        Assert.Equal(status, (await Assert.ThrowsAsync<BillingException>(action)).StatusCode);
    }
}
