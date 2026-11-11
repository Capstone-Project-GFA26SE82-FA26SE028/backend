using Billing.BLL.Implements;
using Billing.Common.DTOs;
using Billing.Common.Exceptions;
using Billing.Common.Settings;
using Billing.DAL.Implements;
using Billing.DAL.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Shared.Persistence.UnitOfWorks;

namespace Billing.Tests;

public sealed class ShiftReportServiceTests
{
    [Fact]
    public async Task ClosingSnapshotsCashBeforeHandoverWithoutPostingCashOut()
    {
        await using var f = await CashPaymentServiceTests.Fixture.Create();
        await f.Pay(300);
        var ledger = CashMovementServiceTests.Make(f.Db, f.Core.Actor);
        await ledger.Create(f.ShiftId, Guid.NewGuid().ToString(), CashMovementServiceTests.Body("CashIn", 200), default);
        await ledger.Create(f.ShiftId, Guid.NewGuid().ToString(), CashMovementServiceTests.Body("CashOut", 100), default);
        var service = Make(f.Db, f.Core.Actor);
        var preview = await service.Preview(f.ShiftId, default);
        Assert.True(preview.CanClose);
        Assert.Equal(1332, preview.ExpectedCash);
        Assert.Equal(232, preview.SaleTotal);
        var result = await service.Close(f.ShiftId, Body(1330, 1000, "Shortage counted"), default);
        Assert.True(result.Created);
        Assert.Equal(1332, result.Report.ExpectedCash);
        Assert.Equal(-2, result.Report.Difference);
        Assert.Equal(330, result.Report.RemainingCash);
        Assert.Equal(f.Core.Actor.Scope.UserId, result.Report.CashierUserRef);
        var shift = await f.Db.CashShifts.SingleAsync();
        Assert.Equal("Closed", shift.Status);
        Assert.Equal(shift.ClosedAt, result.Report.ClosedAt.UtcDateTime);
        Assert.Equal(3, await f.Db.CashMovements.CountAsync());
        Assert.Equal("Paid", (await f.Db.Bills.SingleAsync()).Status);
        Assert.Null((await f.Db.BillCheckouts.SingleAsync()).ReleasedAt);
        Assert.Equal(result.Report.ShiftReportId, (await service.GetForShift(f.ShiftId, default)).ShiftReportId);
        Assert.Equal(result.Report.ShiftReportId, (await service.GetById(result.Report.ShiftReportId, default)).ShiftReportId);
        Assert.Equal(1332, (await service.GetAll(null, 1, 20, default)).Items[0].ExpectedCash);
    }

    [Fact]
    public async Task SameClosingInputsReplayButDifferentInputsCannotOverwriteReport()
    {
        await using var f = await CashPaymentServiceTests.Fixture.Create();
        var service = Make(f.Db, f.Core.Actor);
        var first = await service.Close(f.ShiftId, Body(1000, 800), default);
        var replay = await service.Close(f.ShiftId, Body(1000, 800), default);
        Assert.False(replay.Created);
        Assert.Equal(first.Report.ShiftReportId, replay.Report.ShiftReportId);
        await Status(409, () => service.Close(f.ShiftId, Body(999, 800, "Changed count"), default));
        Assert.Single(await f.Db.ShiftReports.ToListAsync());
        Assert.False((await service.Preview(f.ShiftId, default)).CanClose);
        await Status(409, () => f.Pay(300));
        await Status(409, () => CashMovementServiceTests.Make(f.Db, f.Core.Actor).Create(f.ShiftId,
            Guid.NewGuid().ToString(), CashMovementServiceTests.Body("CashIn", 1), default));
    }

    [Theory]
    [InlineData(999)]
    [InlineData(1001)]
    public async Task BothShortageAndSurplusRequireReason(decimal actual)
    {
        await using var f = await CashPaymentServiceTests.Fixture.Create();
        var service = Make(f.Db, f.Core.Actor);
        await Status(400, () => service.Close(f.ShiftId, Body(actual, 0), default));
        Assert.Empty(await f.Db.ShiftReports.ToListAsync());
        var result = await service.Close(f.ShiftId, Body(actual, 0, "Count explained"), default);
        Assert.Equal(actual - 1000, result.Report.Difference);
    }

    [Fact]
    public async Task CountAndHandoverAreExplicitAndBounded()
    {
        await using var f = await CashPaymentServiceTests.Fixture.Create();
        var service = Make(f.Db, f.Core.Actor);
        foreach (var body in new[] { Body(null, 0), Body(1000, null), Body(-1, 0), Body(1.5m, 0),
            Body(1000, -1), Body(1000, 1001), Body(10000000000000000m, 0), Body(1000, 0, new string('a', 1001)),
            Body(1000, 0, "Bad\nreason"), Body(1000, 0, "Unsupported \u4e00") })
        {
            await Status(400, () => service.Close(f.ShiftId, body, default));
        }

        Assert.Empty(await f.Db.ShiftReports.ToListAsync());
        Assert.Equal("Active", (await f.Db.CashShifts.SingleAsync()).Status);
    }

    [Theory]
    [InlineData("admin", true)]
    [InlineData("manager", false)]
    [InlineData("waiter", false)]
    [InlineData("kitchen", false)]
    public async Task OnlyCashierCanClose(string role, bool administrator)
    {
        await using var f = await CashPaymentServiceTests.Fixture.Create();
        f.Core.Actor.Scope.RoleId = role;
        f.Core.Actor.Scope.IsAdministrator = administrator;
        await Status(403, () => Make(f.Db, f.Core.Actor).Close(f.ShiftId, Body(1000, 0), default));
    }

    [Fact]
    public async Task OwnershipBranchAndReadScopesAreEnforced()
    {
        await using var f = await CashPaymentServiceTests.Fixture.Create();
        var service = Make(f.Db, f.Core.Actor);
        var owner = f.Core.Actor.Scope.UserId;
        f.Core.Actor.Scope.UserId = Guid.NewGuid();
        await Status(403, () => service.Close(f.ShiftId, Body(1000, 0), default));
        f.Core.Actor.Scope.UserId = owner;
        var report = await service.Close(f.ShiftId, Body(1000, 0), default);
        f.Core.Actor.Scope.RoleId = "manager";
        Assert.Single((await service.GetAll(null, 1, 20, default)).Items);
        f.Core.Actor.Scope.RestaurantIds = [];
        Assert.Empty((await service.GetAll(null, 1, 20, default)).Items);
        await Status(403, () => service.GetForShift(f.ShiftId, default));
        await Status(403, () => service.GetById(report.Report.ShiftReportId, default));
        f.Core.Actor.Scope.IsAdministrator = true;
        Assert.Single((await service.GetAll(null, 1, 20, default)).Items);
        await Status(400, () => service.GetAll(null, 0, 20, default));
        await Status(400, () => service.GetById(Guid.Empty, default));
        await Status(404, () => service.GetById(Guid.NewGuid(), default));
    }

    [Fact]
    public async Task UnresolvedPaymentAndMismatchedSalePreventClosingWithoutChangingTheirStatus()
    {
        await using var f = await CashPaymentServiceTests.Fixture.Create();
        await f.Pay(300);
        var payment = await f.Db.Payments.SingleAsync();
        payment.Status = "Pending";
        await f.Db.SaveChangesAsync();
        var service = Make(f.Db, f.Core.Actor);
        Assert.Contains((await service.Preview(f.ShiftId, default)).Blockers,
            value => value.Code == "UnresolvedPayment" && value.PaymentId == payment.PaymentId);
        await Status(409, () => service.Close(f.ShiftId, Body(1232, 0), default));
        Assert.Equal("Pending", (await f.Db.Payments.SingleAsync()).Status);
        payment.Status = "Paid";
        var sale = await f.Db.CashMovements.SingleAsync();
        sale.SignedAmount++;
        await f.Db.SaveChangesAsync();
        Assert.Contains((await service.Preview(f.ShiftId, default)).Blockers, value => value.Code == "InconsistentSale");
        await Status(409, () => service.Close(f.ShiftId, Body(1233, 0), default));
        Assert.Empty(await f.Db.ShiftReports.ToListAsync());
    }

    [Fact]
    public async Task UnsupportedMethodAndHandoverCannotBeAssumedCompleted()
    {
        await using var f = await CashPaymentServiceTests.Fixture.Create();
        await f.Pay(300);
        f.Db.PaymentMethods.Add(new PaymentMethod
        {
            MethodCode = "BankTransfer",
            IsEnabled = true
        });
        var payment = await f.Db.Payments.SingleAsync();
        payment.MethodCode = "BankTransfer";
        await f.Db.SaveChangesAsync();
        var service = Make(f.Db, f.Core.Actor);
        Assert.Contains((await service.Preview(f.ShiftId, default)).Blockers, value => value.Code == "UnsupportedPaymentFlow");
        await Status(409, () => service.Close(f.ShiftId, Body(1232, 0), default));
        payment.MethodCode = "Cash";
        (await f.Db.CashHandovers.SingleAsync()).Status = "Pending";
        await f.Db.SaveChangesAsync();
        Assert.Contains((await service.Preview(f.ShiftId, default)).Blockers, value => value.Code == "UnsupportedCashHandover");
        await Status(409, () => service.Close(f.ShiftId, Body(1232, 0), default));
    }

    [Fact]
    public async Task OrphanSaleAndUnknownLedgerEntriesAreNotSilentlyIgnored()
    {
        await using var f = await CashPaymentServiceTests.Fixture.Create();
        f.Db.CashMovements.Add(new CashMovement
        {
            CashMovementId = Guid.NewGuid(),
            CashShiftId = f.ShiftId,
            Type = "Sale",
            SignedAmount = 10,
            IdempotencyKey = "legacy-sale",
            PostedAt = DateTime.UtcNow
        });
        f.Db.CashMovements.Add(new CashMovement
        {
            CashMovementId = Guid.NewGuid(),
            CashShiftId = f.ShiftId,
            Type = "Unrecognized",
            SignedAmount = 1,
            IdempotencyKey = "legacy-entry",
            PostedAt = DateTime.UtcNow
        });
        await f.Db.SaveChangesAsync();
        var service = Make(f.Db, f.Core.Actor);
        var preview = await service.Preview(f.ShiftId, default);
        Assert.Contains(preview.Blockers, value => value.Code == "UnmatchedSale" && value.CashMovementId.HasValue);
        Assert.Contains(preview.Blockers, value => value.Code == "UnsupportedMovement");
        await Status(409, () => service.Close(f.ShiftId, Body(1011, 0), default));
    }

    [Fact]
    public async Task UnpaidCheckoutNotBoundToShiftDoesNotBlockEmptyCashShiftClose()
    {
        await using var f = await CashPaymentServiceTests.Fixture.Create();
        var shift = await f.Db.CashShifts.SingleAsync();
        shift.OpeningCash = 0;
        await f.Db.SaveChangesAsync();
        var service = Make(f.Db, f.Core.Actor);
        Assert.True((await service.Preview(f.ShiftId, default)).CanClose);
        var report = await service.Close(f.ShiftId, Body(0, 0), default);
        Assert.Equal(0, report.Report.Difference);
        Assert.Equal("Unpaid", (await f.Db.Bills.SingleAsync()).Status);
        Assert.Equal("Ready", (await f.Db.BillCheckouts.SingleAsync()).Status);
        Assert.Null((await f.Db.BillCheckouts.SingleAsync()).ReleasedAt);
    }

    [Fact]
    public async Task DuplicateBillPaymentsAndLedgerKeysRequireReview()
    {
        await using var f = await CashPaymentServiceTests.Fixture.Create();
        await f.Pay(300);
        var original = await f.Db.Payments.SingleAsync();
        f.Db.Payments.Add(new Payment
        {
            PaymentId = Guid.NewGuid(),
            BillId = original.BillId,
            CashShiftId = f.ShiftId,
            MethodCode = "Cash",
            Status = "Paid",
            Amount = 232,
            CashReceived = 300,
            ChangeAmount = 68,
            PaidAt = original.PaidAt,
            ConfirmedByRef = original.ConfirmedByRef,
            VerificationSource = "Cashier",
            IdempotencyKey = "legacy-duplicate-payment"
        });
        f.Db.CashMovements.Add(new CashMovement
        {
            CashMovementId = Guid.NewGuid(),
            CashShiftId = f.ShiftId,
            Type = "CashIn",
            SignedAmount = 1,
            Reason = "Legacy funds",
            PostedAt = DateTime.UtcNow,
            IdempotencyKey = (await f.Db.CashMovements.SingleAsync()).IdempotencyKey
        });
        await f.Db.SaveChangesAsync();
        var service = Make(f.Db, f.Core.Actor);
        var preview = await service.Preview(f.ShiftId, default);
        Assert.Contains(preview.Blockers, value => value.Code == "DuplicateBillPayments");
        Assert.Contains(preview.Blockers, value => value.Code == "DuplicateMovementKey");
        await Status(409, () => service.Close(f.ShiftId, Body(1233, 0), default));
    }

    [Fact]
    public async Task InconsistentLegacyClosedStateIsNotAutomaticallyRepaired()
    {
        await using var f = await CashPaymentServiceTests.Fixture.Create();
        var service = Make(f.Db, f.Core.Actor);
        var shift = await f.Db.CashShifts.SingleAsync();
        shift.Status = "Closed";
        shift.ClosedAt = DateTime.UtcNow;
        await f.Db.SaveChangesAsync();
        await Status(409, () => service.Close(f.ShiftId, Body(1000, 0), default));
        Assert.Empty(await f.Db.ShiftReports.ToListAsync());
        shift = await f.Db.CashShifts.SingleAsync();
        shift.Status = "Active";
        shift.ClosedAt = null;
        f.Db.ShiftReports.Add(new ShiftReport
        {
            ShiftReportId = Guid.NewGuid(),
            CashShiftId = f.ShiftId,
            ExpectedCash = 1000,
            ActualCash = 1000,
            CashHandleOver = 0,
            ClosedAt = DateTime.UtcNow
        });
        await f.Db.SaveChangesAsync();
        Assert.Contains((await service.Preview(f.ShiftId, default)).Blockers, value => value.Code == "ExistingReport");
        await Status(409, () => service.Close(f.ShiftId, Body(1000, 0), default));
        Assert.Single(await f.Db.ShiftReports.ToListAsync());
        Assert.Equal("Active", (await f.Db.CashShifts.SingleAsync()).Status);
    }

    internal static ShiftReportService Make(BillingDbContext db, ShiftIdentity actor, Billing.BLL.Interfaces.IPayOsPaymentService? payOs = null)
    {
        return new ShiftReportService(new ShiftReportRepository(db), new CashShiftRepository(db),
            new CashRegisterRepository(db), new PaymentRepository(db), new CashMovementRepository(db),
            new UnitOfWork<BillingDbContext>(db), actor, Options.Create(new CashRegisterAccessSettings
            {
                CashierRoleIds = ["cashier"],
                ManagerRoleIds = ["manager"]
            }), Options.Create(new CashPaymentSettings
            {
                MethodCode = "Cash"
            }), TimeProvider.System, payOs!);
    }

    internal static CloseCashShiftDTO Body(decimal? actual, decimal? handover, string? reason = null)
    {
        return new CloseCashShiftDTO
        {
            ActualCash = actual,
            CashHandleOver = handover,
            DifferenceReason = reason
        };
    }

    private static async Task Status(int expected, Func<Task> action)
    {
        Assert.Equal(expected, (await Assert.ThrowsAsync<BillingException>(action)).StatusCode);
    }
}
