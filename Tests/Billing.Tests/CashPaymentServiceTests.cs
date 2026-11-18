using Billing.BLL.Implements;
using Billing.BLL.Interfaces;
using Billing.Common.DTOs;
using Billing.Common.Exceptions;
using Billing.Common.Settings;
using Billing.DAL.Implements;
using Billing.DAL.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Shared.Common.Billing;
using Shared.Persistence.UnitOfWorks;

namespace Billing.Tests;

public sealed class CashPaymentServiceTests
{
    [Fact]
    public async Task PaidCheckoutRemainsTerminalDuringGuestRetryAndSynchronization()
    {
        await using var fixture = await Fixture.Create(withHandover: true);
        await fixture.Pay(300);
        var checkout = new BillCheckoutService(new BillCheckoutRepository(fixture.Db), new BillRepository(fixture.Db),
            new UnitOfWork<BillingDbContext>(fixture.Db), fixture.Remote, fixture.Core.Source, fixture.Core.Service,
            fixture.Core.Actor, Options.Create(new CashRegisterAccessSettings
            {
                CashierRoleIds = ["cashier"]
            }), TimeProvider.System, new CashRegisterRepository(fixture.Db), new CashShiftRepository(fixture.Db),
            new TestPaymentReviewClient());
        var sessionId = fixture.Core.Source.Value.TableSessionId;
        Assert.Equal("Paid", (await checkout.GetCurrent(sessionId, "guest-session", default)).Status);
        await Status(409, () => checkout.Request(sessionId, fixture.CheckoutId, "guest-session", default));
        await Status(409, () => checkout.Synchronize(sessionId, fixture.CheckoutId, default));
        Assert.Equal(0, fixture.Remote.ReleaseCalls);
        Assert.Null((await fixture.Db.BillCheckouts.SingleAsync()).ReleasedAt);
    }

    [Fact]
    public async Task CashReceiptStoresNetSaleChangeAndPaidCheckoutAtomically()
    {
        await using var fixture = await Fixture.Create(withHandover: true);
        var result = await fixture.Pay(300);
        Assert.True(result.Created);
        Assert.Equal(232, result.Payment.Amount);
        Assert.Equal(300, result.Payment.CashReceived);
        Assert.Equal(68, result.Payment.ChangeAmount);
        var payment = await fixture.Db.Payments.SingleAsync();
        Assert.Equal("Cashier", payment.VerificationSource);
        Assert.Null(payment.ProviderTransactionId);
        Assert.Null(payment.ProviderOrderCode);
        Assert.Equal(fixture.Core.Actor.Scope.UserId, payment.ConfirmedByRef);
        var sale = await fixture.Db.CashMovements.SingleAsync();
        Assert.Equal("Sale", sale.Type);
        Assert.Equal(232, sale.SignedAmount);
        Assert.Equal(payment.PaymentId, sale.PaymentId);
        Assert.Equal(payment.PaidAt, sale.PostedAt);
        Assert.Equal("Paid", (await fixture.Db.Bills.SingleAsync()).Status);
        var checkout = await fixture.Db.BillCheckouts.SingleAsync();
        Assert.Equal("Paid", checkout.Status);
        Assert.Null(checkout.ReleasedAt);
        Assert.Equal(0, fixture.Remote.ReleaseCalls);
        Assert.Equal(result.Payment.PaymentId, (await fixture.Service.GetById(result.Payment.PaymentId, default)).PaymentId);
    }

    [Fact]
    public async Task ReplayReturnsOriginalReceiptEvenWhenMethodDisabledAndShiftClosed()
    {
        await using var fixture = await Fixture.Create(withHandover: true);
        var first = await fixture.Pay(300);
        (await fixture.Db.PaymentMethods.SingleAsync()).IsEnabled = false;
        (await fixture.Db.CashShifts.SingleAsync()).ClosedAt = DateTime.UtcNow;
        await fixture.Db.SaveChangesAsync();
        var replay = await fixture.Pay(300);
        Assert.False(replay.Created);
        Assert.Equal(first.Payment.PaymentId, replay.Payment.PaymentId);
        Assert.Equal(first.Payment.CashMovementId, replay.Payment.CashMovementId);
        Assert.Single(await fixture.Db.Payments.ToListAsync());
        Assert.Single(await fixture.Db.CashMovements.ToListAsync());
        await Status(409, () => fixture.Pay(301));
        await Status(409, () => fixture.Service.PayCash(fixture.Bill.BillId, Guid.NewGuid().ToString(), fixture.Body(300), default));
    }

    [Theory]
    [InlineData("admin", true)]
    [InlineData("manager", false)]
    [InlineData("waiter", false)]
    [InlineData("kitchen", false)]
    public async Task OtherRolesCannotCollectCash(string role, bool administrator)
    {
        await using var fixture = await Fixture.Create(withHandover: true);
        fixture.Core.Actor.Scope.RoleId = role;
        fixture.Core.Actor.Scope.IsAdministrator = administrator;
        await Status(403, () => fixture.Pay(300));
        Assert.Equal("PendingHandover", (await fixture.Db.Payments.SingleAsync()).Status);
    }

    [Fact]
    public async Task OtherCashierShiftCrossBranchAndRevokedAssignmentsAreRejected()
    {
        await using var fixture = await Fixture.Create(withHandover: true);
        var original = fixture.Core.Actor.Scope.UserId;
        fixture.Core.Actor.Scope.UserId = Guid.NewGuid();
        await Status(403, () => fixture.Pay(300));
        fixture.Core.Actor.Scope.UserId = original;
        fixture.Core.Actor.Scope.RestaurantIds = [];
        await Status(403, () => fixture.Pay(300));
        fixture.Core.Actor.Scope.RestaurantIds = [fixture.Bill.RestaurantId];
        (await fixture.Db.CashRegisters.SingleAsync()).RestaurantId = Guid.NewGuid();
        await fixture.Db.SaveChangesAsync();
        await Status(403, () => fixture.Pay(300));
    }

    [Fact]
    public async Task ClosedOrInconsistentShiftCannotConfirmAndDisabledMethodDoesNotAbandonPendingCash()
    {
        await using var fixture = await Fixture.Create(withHandover: true);
        var shift = await fixture.Db.CashShifts.SingleAsync();
        shift.ClosedAt = DateTime.UtcNow;
        await fixture.Db.SaveChangesAsync();
        await Status(409, () => fixture.Pay(300));
        shift.ClosedAt = null;
        shift.Status = "Closed";
        await fixture.Db.SaveChangesAsync();
        await Status(409, () => fixture.Pay(300));
        shift.Status = "Active";
        var method = await fixture.Db.PaymentMethods.SingleAsync();
        method.IsEnabled = false;
        await fixture.Db.SaveChangesAsync();
        Assert.True((await fixture.Pay(300)).Created);
        Assert.Single(await fixture.Db.CashMovements.ToListAsync());
    }

    [Fact]
    public async Task InvalidCashAmountsAndMismatchedCashLeavePendingPaymentUntouched()
    {
        await using var fixture = await Fixture.Create(withHandover: true);
        foreach (var value in new decimal?[] { null, -1, 300.5m, 10000000000000000m })
        {
            await Status(400, () => fixture.Pay(value));
        }

        await Status(400, () => fixture.Service.PayCash(fixture.Bill.BillId, "not-a-uuid", fixture.Body(300), default));
        await Status(409, () => fixture.Pay(231));
        Assert.Equal("PendingHandover", (await fixture.Db.Payments.SingleAsync()).Status);
        Assert.Empty(await fixture.Db.CashMovements.ToListAsync());
    }

    [Fact]
    public async Task PendingStaleOrMismatchedCheckoutCannotChargeOldBill()
    {
        await using var fixture = await Fixture.Create(withHandover: true);
        var checkout = await fixture.Db.BillCheckouts.SingleAsync();
        checkout.Status = "WaitingConfirmation";
        await fixture.Db.SaveChangesAsync();
        await Status(409, () => fixture.Pay(300));
        checkout = await fixture.Db.BillCheckouts.SingleAsync();
        checkout.Status = "Presented";
        await fixture.Db.SaveChangesAsync();
        fixture.Core.Source.Value.Items[0].Quantity++;
        await Status(409, () => fixture.Pay(300));
        fixture.Core.Source.Value.Items[0].Quantity--;
        checkout = await fixture.Db.BillCheckouts.SingleAsync();
        checkout.SourceHash = new string('A', 64);
        await fixture.Db.SaveChangesAsync();
        await Status(409, () => fixture.Pay(300));
        Assert.Equal("PendingHandover", (await fixture.Db.Payments.SingleAsync()).Status);
    }

    [Fact]
    public async Task ZeroTotalAndNonVndBillsCannotBeCollectedInThisIncrement()
    {
        await using var fixture = await Fixture.Create(withHandover: true);
        var bill = await fixture.Db.Bills.SingleAsync();
        bill.TotalAmount = 0;
        await fixture.Db.SaveChangesAsync();
        await Status(409, () => fixture.Pay(300));
        bill.TotalAmount = 232;
        bill.Currency = "USD";
        await fixture.Db.SaveChangesAsync();
        await Status(409, () => fixture.Pay(300));
        Assert.Equal("PendingHandover", (await fixture.Db.Payments.SingleAsync()).Status);
        Assert.Empty(await fixture.Db.CashMovements.ToListAsync());
    }

    [Fact]
    public async Task OrderingFailureRollsBackPaymentMovementBillAndCheckout()
    {
        await using var fixture = await Fixture.Create(withHandover: true);
        fixture.Remote.Failure = new BillingException(503, "Ordering unavailable.");
        await Status(503, () => fixture.Pay(300));
        Assert.Equal("PendingHandover", (await fixture.Db.Payments.SingleAsync()).Status);
        Assert.Empty(await fixture.Db.CashMovements.ToListAsync());
        Assert.Equal("Unpaid", (await fixture.Db.Bills.AsNoTracking().SingleAsync()).Status);
        Assert.Equal("Presented", (await fixture.Db.BillCheckouts.AsNoTracking().SingleAsync()).Status);
        fixture.Remote.Failure = null;
        Assert.True((await fixture.Pay(300)).Created);
    }

    [Fact]
    public async Task ReceiptReadScopeMatchesBillingAndCannotBeReadByAnotherBranch()
    {
        await using var fixture = await Fixture.Create(withHandover: true);
        var paid = await fixture.Pay(300);
        fixture.Core.Actor.Scope.RoleId = "manager";
        Assert.Equal(paid.Payment.PaymentId, (await fixture.Service.GetById(paid.Payment.PaymentId, default)).PaymentId);
        fixture.Core.Actor.Scope.RestaurantIds = [];
        await Status(403, () => fixture.Service.GetById(paid.Payment.PaymentId, default));
        fixture.Core.Actor.Scope.IsAdministrator = true;
        Assert.Equal(paid.Payment.PaymentId, (await fixture.Service.GetById(paid.Payment.PaymentId, default)).PaymentId);
    }

    private static async Task Status(int expected, Func<Task> action)
    {
        Assert.Equal(expected, (await Assert.ThrowsAsync<BillingException>(action)).StatusCode);
    }

    internal sealed class Fixture : IAsyncDisposable
    {
        public BillServiceTests.BillFixture Core { get; }
        public BillServiceTests.BillTestDb Db => Core.Db;
        public BillDTO Bill { get; private set; } = null!;
        public Guid CheckoutId { get; } = Guid.NewGuid();
        public Guid ShiftId { get; } = Guid.NewGuid();
        public string Key { get; } = Guid.NewGuid().ToString();
        public Guid PaymentId { get; private set; }
        public CheckoutSource Remote { get; } = new();
        public PaymentService Service { get; }

        private Fixture(BillServiceTests.BillFixture core)
        {
            Core = core;
            Service = MakeService(core.Db, core.Actor, core.Service, Remote);
        }

        public static PaymentService MakeService(BillingDbContext db, IBillingAccess actor, IBillService billService, ICheckoutSourceClient remote)
        {
            return new PaymentService(new PaymentRepository(db), new BillRepository(db), new BillCheckoutRepository(db),
                new CashShiftRepository(db), new CashRegisterRepository(db),
                new CashMovementRepository(db), new UnitOfWork<BillingDbContext>(db), actor, billService, remote,
                Options.Create(new CashRegisterAccessSettings
                {
                    CashierRoleIds = ["cashier"],
                    ManagerRoleIds = ["manager"]
                }), Options.Create(new CashPaymentSettings
                {
                    MethodCode = "Cash"
                }), TimeProvider.System);
        }

        public static async Task<Fixture> Create(bool withHandover = false)
        {
            var fixture = new Fixture(await BillServiceTests.BillFixture.Create());
            fixture.Bill = await fixture.Core.Service.Create(fixture.Core.Source.Value.TableSessionId, default);
            fixture.Db.CashShifts.Add(new CashShift
            {
                CashShiftId = fixture.ShiftId,
                CashRegister = new CashRegister
                {
                    CashRegisterId = Guid.NewGuid(),
                    RestaurantId = fixture.Bill.RestaurantId,
                    Code = "MAIN"
                },
                CashierUserRef = fixture.Core.Actor.Scope.UserId,
                Status = "Active",
                OpeningCash = 1000,
                OpenedAt = DateTime.UtcNow
            });
            fixture.Db.PaymentMethods.Add(new PaymentMethod
            {
                MethodCode = "Cash",
                IsEnabled = true
            });
            fixture.Db.BillCheckouts.Add(new BillCheckout
            {
                CheckoutId = fixture.CheckoutId,
                BillId = fixture.Bill.BillId,
                TableSessionRef = fixture.Core.Source.Value.TableSessionId,
                Status = "Ready",
                CreatedAt = DateTime.UtcNow,
                SourceHash = fixture.Bill.Items[0].Item.SourceFingerprint
            });
            await fixture.Db.SaveChangesAsync();
            if (withHandover)
            {
                fixture.PaymentId = await CashPaymentSqlServerTests.SeedPendingHandover(fixture.Db, fixture.Bill.BillId, fixture.Body(300), fixture.Key);
            }

            return fixture;
        }

        public CreateCashPaymentDTO Body(decimal? received)
        {
            return new CreateCashPaymentDTO
            {
                CheckoutId = CheckoutId,
                CashShiftId = ShiftId,
                CashReceived = received
            };
        }

        public async Task RecordCash(decimal amount)
        {
            var checkout = await Db.BillCheckouts.SingleAsync();
            var previousStatus = checkout.Status;
            if (previousStatus == "Ready")
            {
                var shift = await Db.CashShifts.SingleAsync();
                var now = DateTime.UtcNow;
                checkout.Status = "Presented";
                checkout.CashRegisterId = shift.CashRegisterId;
                checkout.SubmittedAt = now;
                checkout.PreparedAt = now;
                checkout.PreparedByRef = shift.CashierUserRef;
                checkout.PresentedAt = now;
                await Db.SaveChangesAsync();
            }

            var actor = new ShiftIdentity
            {
                Scope = new BillingAccessScope
                {
                    UserId = Guid.NewGuid(),
                    RoleId = "waiter",
                    RestaurantIds = [Bill.RestaurantId]
                }
            };
            var handover = new CashHandoverService(new CashHandoverRepository(Db), new PaymentRepository(Db),
                new BillRepository(Db), new BillCheckoutRepository(Db), new CashRegisterRepository(Db),
                new CashShiftRepository(Db), new PaymentMethodRepository(Db), new UnitOfWork<BillingDbContext>(Db),
                actor, new PresentedBillReview(Bill.RestaurantId), Service, Options.Create(new CashRegisterAccessSettings
                {
                    WaiterRoleIds = ["waiter"]
                }), Options.Create(new CashPaymentSettings
                {
                    MethodCode = "Cash"
                }), TimeProvider.System, new TestPaidSessionClosureClient());
            try
            {
                var result = await handover.Create(Bill.BillId, Key, new CreateCashHandoverDTO
                {
                    CheckoutId = CheckoutId,
                    CashReceived = amount
                }, default);
                PaymentId = result.Handover.PaymentId;
            }
            catch (BillingException) when (previousStatus == "Ready")
            {
                var current = await Db.BillCheckouts.SingleAsync();
                current.Status = previousStatus;
                current.CashRegisterId = null;
                current.SubmittedAt = null;
                current.PreparedAt = null;
                current.PreparedByRef = null;
                current.PresentedAt = null;
                await Db.SaveChangesAsync();
                throw;
            }
        }

        public Task<(PaymentDTO Payment, bool Created)> Pay(decimal? received)
        {
            return Service.ConfirmCashHandover(PaymentId, Bill.BillId, Key, Body(received), default);
        }

        public ValueTask DisposeAsync()
        {
            return Core.DisposeAsync();
        }
    }

    private sealed class PresentedBillReview(Guid restaurantId) : IPaymentReviewClient
    {
        public Task<Guid> GetResultRestaurantAsync(Guid sessionId, Guid requestId, CancellationToken ct)
        {
            return Task.FromResult(restaurantId);
        }

        public Task<(Guid RestaurantId, int PendingOrderCount)> GetReviewAsync(Guid sessionId, Guid requestId, CancellationToken ct)
        {
            return Task.FromResult((restaurantId, 0));
        }
    }

    internal sealed class CheckoutSource : ICheckoutSourceClient
    {
        public BillingException? Failure { get; set; }
        public int ReleaseCalls { get; private set; }

        public Task VerifyAsync(Guid sessionId, string guestToken, CancellationToken ct)
        {
            return Task.CompletedTask;
        }

        public Task<BillCheckoutDTO> BeginAsync(Guid sessionId, Guid id, string guestToken, CancellationToken ct)
        {
            throw new NotSupportedException("Payment must not start or replace checkout.");
        }

        public Task ReleaseAsync(Guid sessionId, Guid id, CancellationToken ct)
        {
            ReleaseCalls++;
            return Task.CompletedTask;
        }

        public Task BindAsync(Guid sessionId, Guid id, Guid billId, string hash, CancellationToken ct)
        {
            if (Failure is not null)
            {
                throw Failure;
            }

            return Task.CompletedTask;
        }
    }
}
