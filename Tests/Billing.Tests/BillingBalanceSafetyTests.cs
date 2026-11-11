using Billing.Common.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace Billing.Tests;

public sealed class BillingBalanceSafetyTests
{
    [Theory]
    [InlineData("9999999999999998", true)]
    [InlineData("9999999999999999", false)]
    public async Task CashInValidatesResultingBalanceBeforePosting(string opening, bool allowed)
    {
        await using var fixture = await CashPaymentServiceTests.Fixture.Create();
        var shift = await fixture.Db.CashShifts.SingleAsync();
        shift.OpeningCash = decimal.Parse(opening, System.Globalization.CultureInfo.InvariantCulture);
        await fixture.Db.SaveChangesAsync();
        var service = CashMovementServiceTests.Make(fixture.Db, fixture.Core.Actor);
        var action = () => service.Create(fixture.ShiftId, Guid.NewGuid().ToString(),
            CashMovementServiceTests.Body("CashIn", 1), default);
        if (allowed)
        {
            Assert.True((await action()).Created);
            Assert.Single(await fixture.Db.CashMovements.ToListAsync());
        }
        else
        {
            Assert.Equal(409, (await Assert.ThrowsAsync<BillingException>(action)).StatusCode);
            Assert.Empty(await fixture.Db.CashMovements.ToListAsync());
        }
    }

    [Theory]
    [InlineData("9999999999999767", true)]
    [InlineData("9999999999999768", false)]
    public async Task CashSaleEitherCommitsEverythingOrRejectsWithoutPaidState(string opening, bool allowed)
    {
        await using var fixture = await CashPaymentServiceTests.Fixture.Create();
        var shift = await fixture.Db.CashShifts.SingleAsync();
        shift.OpeningCash = decimal.Parse(opening, System.Globalization.CultureInfo.InvariantCulture);
        await fixture.Db.SaveChangesAsync();
        var persistedOpening = await fixture.Db.CashShifts.AsNoTracking().Select(value => value.OpeningCash).SingleAsync();
        if (allowed)
        {
            Assert.True((await fixture.Pay(300)).Created);
        }
        else
        {
            Assert.Equal(409, (await Assert.ThrowsAsync<BillingException>(() => fixture.Pay(300))).StatusCode);
        }

        fixture.Db.ChangeTracker.Clear();
        Assert.Single(await fixture.Db.Payments.ToListAsync());
        Assert.Equal(allowed ? "Paid" : "PendingHandover", (await fixture.Db.Payments.SingleAsync()).Status);
        Assert.Equal(allowed ? 1 : 0, await fixture.Db.CashMovements.CountAsync());
        Assert.Equal(allowed ? "Paid" : "Unpaid", (await fixture.Db.Bills.SingleAsync()).Status);
        Assert.Equal(allowed ? "Paid" : "Ready", (await fixture.Db.BillCheckouts.SingleAsync()).Status);
        Assert.Equal(persistedOpening,
            (await fixture.Db.CashShifts.SingleAsync()).OpeningCash);
    }
}
