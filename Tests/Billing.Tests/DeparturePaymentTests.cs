using Billing.Common.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace Billing.Tests;

public sealed class DeparturePaymentTests
{
    [Fact]
    public async Task CashMustBeConfirmedAndAnyAssignedWaiterCanVerifyAfterShiftHasClosed()
    {
        await using var f = await CashPaymentServiceTests.Fixture.Create();
        var actor = f.Core.Actor.Scope;
        actor.RoleId = "waiter";
        await Error(409, () => f.Service.RequirePaid(f.Core.Source.Value.TableSessionId, f.Bill.RestaurantId, default));
        actor.RoleId = "cashier";
        await f.Collect(300);
        actor.RoleId = "waiter";
        await Error(409, () => f.Service.RequirePaid(f.Core.Source.Value.TableSessionId, f.Bill.RestaurantId, default));
        actor.RoleId = "cashier";
        await f.Handovers(f.Core.Actor).Confirm((await f.Db.Payments.SingleAsync()).PaymentId, 300, default);
        actor.RoleId = "waiter";
        actor.UserId = Guid.NewGuid();
        (await f.Db.CashShifts.SingleAsync()).Status = "Closed";
        (await f.Db.CashShifts.SingleAsync()).ClosedAt = DateTime.UtcNow;
        await f.Db.SaveChangesAsync();
        await f.Service.RequirePaid(f.Core.Source.Value.TableSessionId, f.Bill.RestaurantId, default);
        Assert.Single(await f.Db.CashMovements.ToListAsync());
        Assert.Null((await f.Db.BillCheckouts.SingleAsync()).ReleasedAt);
    }

    [Theory]
    [InlineData("cashier")]
    [InlineData("manager")]
    [InlineData("admin")]
    public async Task OtherRolesCannotAuthorizeDeparture(string role)
    {
        await using var f = await CashPaymentServiceTests.Fixture.Create();
        f.Core.Actor.Scope.RoleId = role;
        f.Core.Actor.Scope.IsAdministrator = role == "admin";
        await Error(403, () => f.Service.RequirePaid(f.Core.Source.Value.TableSessionId, f.Bill.RestaurantId, default));
    }

    [Fact]
    public async Task PaidBillWithoutReceiptOrWithMismatchedHashCannotAuthorizeDeparture()
    {
        await using var f = await CashPaymentServiceTests.Fixture.Create();
        f.Core.Actor.Scope.RoleId = "waiter";
        var checkout = await f.Db.BillCheckouts.SingleAsync();
        checkout.Status = "Paid";
        var bill = await f.Db.Bills.SingleAsync();
        bill.Status = "Paid";
        bill.PaidAt = DateTime.UtcNow;
        await f.Db.SaveChangesAsync();
        await Error(409, () => f.Service.RequirePaid(f.Core.Source.Value.TableSessionId, f.Bill.RestaurantId, default));
        checkout.SourceHash = new string('F', 64);
        await f.Db.SaveChangesAsync();
        await Error(409, () => f.Service.RequirePaid(f.Core.Source.Value.TableSessionId, f.Bill.RestaurantId, default));
        await Error(403, () => f.Service.RequirePaid(f.Core.Source.Value.TableSessionId, Guid.NewGuid(), default));
    }

    [Fact]
    public async Task BankTransferMustHaveVerifiedReceiptAndReviewRequiredBlocksDeparture()
    {
        await using var f = await PayOsPaymentServiceTests.Fixture.Create();
        var created = await f.CreatePayment();
        f.Core.Core.Actor.Scope.RoleId = "waiter";
        await Error(409, () => f.Core.Service.RequirePaid(f.Core.Core.Source.Value.TableSessionId, f.Core.Bill.RestaurantId, default));
        f.Core.Core.Actor.Scope.RoleId = "cashier";
        f.Provider.Settle(232);
        await f.Service.Reconcile(created.Payment.PaymentId, default);
        f.Core.Core.Actor.Scope.RoleId = "waiter";
        await f.Core.Service.RequirePaid(f.Core.Core.Source.Value.TableSessionId, f.Core.Bill.RestaurantId, default);
        var payment = await f.Core.Db.Payments.SingleAsync();
        payment.Status = "ReviewRequired";
        await f.Core.Db.SaveChangesAsync();
        await Error(409, () => f.Core.Service.RequirePaid(f.Core.Core.Source.Value.TableSessionId, f.Core.Bill.RestaurantId, default));
        Assert.Equal("Paid", (await f.Core.Db.Bills.SingleAsync()).Status);
        Assert.Null((await f.Core.Db.BillCheckouts.SingleAsync()).ReleasedAt);
    }

    [Fact]
    public async Task MissingOrInconsistentCashSaleCannotAuthorizeDeparture()
    {
        await using var f = await CashPaymentServiceTests.Fixture.Create();
        await f.Collect(300);
        await f.Handovers(f.Core.Actor).Confirm((await f.Db.Payments.SingleAsync()).PaymentId, 300, default);
        f.Core.Actor.Scope.RoleId = "waiter";
        var movement = await f.Db.CashMovements.SingleAsync();
        movement.SignedAmount -= 1;
        await f.Db.SaveChangesAsync();
        await Error(409, () => f.Service.RequirePaid(f.Core.Source.Value.TableSessionId, f.Bill.RestaurantId, default));
        f.Db.CashMovements.Remove(movement);
        await f.Db.SaveChangesAsync();
        await Error(409, () => f.Service.RequirePaid(f.Core.Source.Value.TableSessionId, f.Bill.RestaurantId, default));
    }

    private static async Task Error(int status, Func<Task> action)
    {
        Assert.Equal(status, (await Assert.ThrowsAsync<BillingException>(action)).StatusCode);
    }
}
