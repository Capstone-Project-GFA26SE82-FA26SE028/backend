using Billing.Common.DTOs;
using Billing.Common.Exceptions;
using Billing.DAL.Implements;
using Microsoft.EntityFrameworkCore;

namespace Billing.Tests;

public sealed class PayOsClosingSafetyTests
{
    [Fact]
    public async Task PaidTransfersArePolledAndExtraMoneyBlocksCloseWithoutSecondSettlement()
    {
        await using var fixture = await PayOsPaymentServiceTests.Fixture.Create();
        var created = await fixture.CreatePayment();
        fixture.Provider.Settle(232);
        await fixture.Service.Reconcile(created.Payment.PaymentId, default);
        var batch = await new PaymentRepository(fixture.Core.Db).GetProviderBatchAsync(0, 100, DateTime.UtcNow.AddHours(-24), default);
        Assert.Contains(created.Payment.PaymentId, batch.Ids);
        fixture.Provider.Value = fixture.Provider.Value! with
        {
            AmountPaid = 242,
            AmountRemaining = -10,
            Transactions = [new PayOsTransactionDTO("BANK-001", 232), new PayOsTransactionDTO("BANK-002", 10)]
        };
        var reports = ShiftReportServiceTests.Make(fixture.Core.Db, fixture.Core.Core.Actor, fixture.Service);
        Assert.Equal(409, (await Assert.ThrowsAsync<BillingException>(() => reports.Close(fixture.Core.ShiftId,
            ShiftReportServiceTests.Body(1000, 0), default))).StatusCode);
        fixture.Core.Db.ChangeTracker.Clear();
        Assert.Equal("ReviewRequired", (await fixture.Core.Db.Payments.SingleAsync()).Status);
        Assert.Equal("Paid", (await fixture.Core.Db.Bills.SingleAsync()).Status);
        Assert.Equal("Paid", (await fixture.Core.Db.BillCheckouts.SingleAsync()).Status);
        Assert.Equal("Active", (await fixture.Core.Db.CashShifts.SingleAsync()).Status);
        Assert.Empty(await fixture.Core.Db.ShiftReports.ToListAsync());
        Assert.Empty(await fixture.Core.Db.CashMovements.ToListAsync());
    }

    [Fact]
    public async Task ProviderTimeoutBlocksCloseAndRetryVerifiesBeforeClosing()
    {
        await using var fixture = await PayOsPaymentServiceTests.Fixture.Create();
        var created = await fixture.CreatePayment();
        fixture.Provider.Settle(232);
        await fixture.Service.Reconcile(created.Payment.PaymentId, default);
        fixture.Provider.Offline = true;
        var reports = ShiftReportServiceTests.Make(fixture.Core.Db, fixture.Core.Core.Actor, fixture.Service);
        Assert.Equal(409, (await Assert.ThrowsAsync<BillingException>(() => reports.Close(fixture.Core.ShiftId,
            ShiftReportServiceTests.Body(1000, 0), default))).StatusCode);
        Assert.Empty(await fixture.Core.Db.ShiftReports.ToListAsync());
        fixture.Provider.Offline = false;
        Assert.True((await reports.Close(fixture.Core.ShiftId, ShiftReportServiceTests.Body(1000, 0), default)).Created);
        fixture.Provider.Offline = true;
        Assert.False((await reports.Close(fixture.Core.ShiftId, ShiftReportServiceTests.Body(1000, 0), default)).Created);
        Assert.Single(await fixture.Core.Db.Payments.ToListAsync());
        Assert.Single(await fixture.Core.Db.ShiftReports.ToListAsync());
    }

    [Fact]
    public async Task ClosedShiftPaidPaymentStaysInWindowButOldClosedPaymentDoesNot()
    {
        await using var fixture = await PayOsPaymentServiceTests.Fixture.Create();
        var created = await fixture.CreatePayment();
        fixture.Provider.Settle(232);
        await fixture.Service.Reconcile(created.Payment.PaymentId, default);
        var reports = ShiftReportServiceTests.Make(fixture.Core.Db, fixture.Core.Core.Actor, fixture.Service);
        await reports.Close(fixture.Core.ShiftId, ShiftReportServiceTests.Body(1000, 0), default);
        var repository = new PaymentRepository(fixture.Core.Db);
        Assert.Contains(created.Payment.PaymentId, (await repository.GetProviderBatchAsync(0, 100, DateTime.UtcNow.AddHours(-24), default)).Ids);
        Assert.DoesNotContain(created.Payment.PaymentId, (await repository.GetProviderBatchAsync(0, 100, DateTime.UtcNow.AddHours(1), default)).Ids);
    }
}
