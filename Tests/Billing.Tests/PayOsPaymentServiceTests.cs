using Billing.BLL.Implements;
using Billing.BLL.Interfaces;
using Billing.Common.DTOs;
using Billing.Common.Exceptions;
using Billing.Common.Settings;
using Billing.DAL.Implements;
using Billing.DAL.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Shared.Persistence.UnitOfWorks;

namespace Billing.Tests;

public sealed class PayOsPaymentServiceTests
{
    [Fact]
    public async Task CreatingPayOsLinkRequiresPresentedBillBeforeCallingProvider()
    {
        await using var fixture = await Fixture.Create();
        var checkout = await fixture.Core.Db.BillCheckouts.SingleAsync();
        checkout.PresentedAt = null;
        await fixture.Core.Db.SaveChangesAsync();
        await Status(409, () => fixture.CreatePayment());
        Assert.Equal(0, fixture.Provider.CreateCalls);
        Assert.Empty(await fixture.Core.Db.Payments.ToListAsync());
        Assert.Equal("Unpaid", (await fixture.Core.Db.Bills.SingleAsync()).Status);
    }

    [Fact]
    public async Task CreateAndReplayKeepOneProviderOrderAndPersistRealLinkDetails()
    {
        await using var fixture = await Fixture.Create();
        var first = await fixture.CreatePayment();
        var second = await fixture.CreatePayment();
        Assert.True(first.Created);
        Assert.False(second.Created);
        Assert.Equal(first.Payment.PaymentId, second.Payment.PaymentId);
        Assert.Equal(first.Payment.ProviderOrderCode, second.Payment.ProviderOrderCode);
        Assert.Equal("Pending", first.Payment.Status);
        Assert.Equal("https://pay.example/link", first.Payment.CheckoutUrl);
        Assert.Equal(1, fixture.Provider.CreateCalls);
        Assert.Single(await fixture.Core.Db.Payments.ToListAsync());
        Assert.Empty(await fixture.Core.Db.CashMovements.ToListAsync());
    }

    [Fact]
    public async Task ProviderTimeoutRemainsUnknownBlocksCashAndRestartsWithSameOrderCode()
    {
        await using var fixture = await Fixture.Create();
        fixture.Provider.Offline = true;
        var first = await fixture.CreatePayment();
        Assert.Equal("Unknown", first.Payment.Status);
        await Status(409, () => fixture.Core.Pay(300));
        fixture.Provider.Offline = false;
        var recovered = await fixture.CreatePayment();
        Assert.False(recovered.Created);
        Assert.Equal(first.Payment.ProviderOrderCode, recovered.Payment.ProviderOrderCode);
        Assert.Equal("Pending", recovered.Payment.Status);
        Assert.Single(await fixture.Core.Db.Payments.ToListAsync());
    }

    [Fact]
    public async Task VerifiedPaymentSettlesExactlyOnceWithoutCashOrCheckoutRelease()
    {
        await using var fixture = await Fixture.Create();
        var first = await fixture.CreatePayment();
        fixture.Provider.Settle(232);
        var paid = await fixture.Service.Reconcile(first.Payment.PaymentId, default);
        Assert.Equal("Paid", paid.Status);
        Assert.Equal(232, paid.ProviderAmountPaid);
        Assert.Equal("Paid", (await fixture.Core.Db.Bills.AsNoTracking().SingleAsync()).Status);
        var checkout = await fixture.Core.Db.BillCheckouts.AsNoTracking().SingleAsync();
        Assert.Equal("Paid", checkout.Status);
        Assert.Null(checkout.ReleasedAt);
        Assert.Empty(await fixture.Core.Db.CashMovements.ToListAsync());
        Assert.Equal(0, fixture.Core.Remote.ReleaseCalls);
        var again = await fixture.Service.Reconcile(first.Payment.PaymentId, default);
        Assert.Equal(paid.PaidAt, again.PaidAt);
        Assert.Null((await fixture.Core.Db.Payments.SingleAsync()).ConfirmedByRef);
        await Status(409, () => fixture.Core.Pay(300));
    }

    [Fact]
    public async Task ConfirmedZeroAllowsCashButLateTransferRequiresReviewAndNeverPaysTwice()
    {
        await using var fixture = await Fixture.Create();
        var first = await fixture.CreatePayment();
        var cancelled = await fixture.Service.Cancel(first.Payment.PaymentId, default);
        Assert.True(cancelled.IsVerifiedZero);
        Assert.Equal("Cancelled", cancelled.Status);
        var cash = await fixture.Core.Pay(300);
        var originalPaidAt = cash.Payment.PaidAt;
        fixture.Provider.Settle(232);
        var late = await fixture.Service.Reconcile(first.Payment.PaymentId, default);
        Assert.True(late.RequiresReview);
        Assert.Null(late.PaidAt);
        Assert.Equal(originalPaidAt.UtcDateTime, (await fixture.Core.Db.Bills.AsNoTracking().SingleAsync()).PaidAt);
        Assert.Single(await fixture.Core.Db.CashMovements.ToListAsync());
    }

    [Theory]
    [InlineData(1)]
    [InlineData(231)]
    [InlineData(233)]
    public async Task PartialOrExcessMoneyNeverMarksBillPaid(long amount)
    {
        await using var fixture = await Fixture.Create();
        var first = await fixture.CreatePayment();
        fixture.Provider.Settle(amount);
        var result = await fixture.Service.Reconcile(first.Payment.PaymentId, default);
        Assert.True(result.RequiresReview);
        Assert.Equal("Unpaid", (await fixture.Core.Db.Bills.AsNoTracking().SingleAsync()).Status);
        await Status(409, () => fixture.Core.Pay(300));
    }

    [Fact]
    public async Task LocalDeadlineAloneCannotConcludeZeroAndCancellationTimeoutStaysFrozen()
    {
        await using var fixture = await Fixture.Create();
        var first = await fixture.CreatePayment();
        var row = await fixture.Core.Db.Payments.SingleAsync();
        row.ExpiredAt = DateTime.UtcNow.AddMinutes(-1);
        await fixture.Core.Db.SaveChangesAsync();
        fixture.Provider.CancelTimeout = true;
        var result = await fixture.Service.Reconcile(first.Payment.PaymentId, default);
        Assert.Equal("CancelRequested", result.Status);
        Assert.False(result.IsVerifiedZero);
        await Status(409, () => fixture.Core.Pay(300));
        fixture.Provider.CancelTimeout = false;
        result = await fixture.Service.Reconcile(first.Payment.PaymentId, default);
        Assert.True(result.IsVerifiedZero);
        Assert.Equal("Expired", result.Status);
    }

    [Fact]
    public async Task DisabledCreationDoesNotDisableRecoveryOfExistingPayments()
    {
        await using var fixture = await Fixture.Create();
        var first = await fixture.CreatePayment();
        fixture.Settings.Enabled = false;
        fixture.Provider.Settle(232);
        Assert.Equal("Paid", (await fixture.Service.Reconcile(first.Payment.PaymentId, default)).Status);
        Assert.False((await fixture.CreatePayment()).Created);
    }

    [Theory]
    [InlineData("admin", true)]
    [InlineData("manager", false)]
    [InlineData("waiter", false)]
    public async Task OnlyCashierCanCreate(string role, bool administrator)
    {
        await using var fixture = await Fixture.Create();
        fixture.Core.Core.Actor.Scope.RoleId = role;
        fixture.Core.Core.Actor.Scope.IsAdministrator = administrator;
        await Status(403, () => fixture.CreatePayment());
        Assert.Equal(0, fixture.Provider.CreateCalls);
    }

    [Fact]
    public async Task WrongOwnerBranchAndDisabledMethodAreRejectedBeforeProviderCall()
    {
        await using var fixture = await Fixture.Create();
        fixture.Core.Core.Actor.Scope.RestaurantIds = [];
        await Status(403, () => fixture.CreatePayment());
        fixture.Core.Core.Actor.Scope.RestaurantIds = [fixture.Core.Bill.RestaurantId];
        var owner = fixture.Core.Core.Actor.Scope.UserId;
        fixture.Core.Core.Actor.Scope.UserId = Guid.NewGuid();
        await Status(403, () => fixture.CreatePayment());
        fixture.Core.Core.Actor.Scope.UserId = owner;
        var method = await fixture.Core.Db.PaymentMethods.SingleAsync(value => value.MethodCode == "BankTransfer");
        method.IsEnabled = false;
        await fixture.Core.Db.SaveChangesAsync();
        await Status(409, () => fixture.CreatePayment());
        Assert.Equal(0, fixture.Provider.CreateCalls);
    }

    [Fact]
    public async Task InvalidKeyOrConfigurationDoesNotPersistAttempt()
    {
        await using var fixture = await Fixture.Create();
        await Status(400, () => fixture.CreatePayment("not-uuid"));
        fixture.Settings.Enabled = false;
        await Status(409, () => fixture.CreatePayment());
        Assert.Empty(await fixture.Core.Db.Payments.ToListAsync());
    }

    [Fact]
    public async Task WebhookReplayIsDeduplicatedAndProviderMustConfirmSettlement()
    {
        await using var fixture = await Fixture.Create();
        var first = await fixture.CreatePayment();
        fixture.Provider.Webhook = fixture.Event(232);
        await fixture.Webhooks.Process("signed-test", default);
        Assert.Equal("Pending", (await fixture.Service.GetById(first.Payment.PaymentId, default)).Status);
        fixture.Provider.Settle(232);
        await fixture.Webhooks.Process("signed-test", default);
        Assert.Equal("Paid", (await fixture.Service.GetById(first.Payment.PaymentId, default)).Status);
        await fixture.Webhooks.Process("signed-test", default);
        Assert.Single(await fixture.Core.Db.PaymentWebhooks.ToListAsync());
        Assert.Equal("Applied", (await fixture.Core.Db.PaymentWebhooks.SingleAsync()).ProcessingStatus);
        Assert.Empty(await fixture.Core.Db.CashMovements.ToListAsync());
    }

    [Fact]
    public async Task InvalidOrOversizedWebhookDoesNotCreateAuditOrPayment()
    {
        await using var fixture = await Fixture.Create();
        await Status(400, () => fixture.Webhooks.Process("invalid", default));
        await Status(400, () => fixture.Webhooks.Process(new string('x', 65537), default));
        Assert.Empty(await fixture.Core.Db.PaymentWebhooks.ToListAsync());
    }

    [Fact]
    public async Task ConflictingSignedWebhookRequiresReviewWithoutAutomaticSettlement()
    {
        await using var fixture = await Fixture.Create();
        var first = await fixture.CreatePayment();
        fixture.Provider.Webhook = fixture.Event(232);
        await fixture.Webhooks.Process("signed-test", default);
        fixture.Provider.Webhook = fixture.Event(233) with
        {
            PayloadHash = new string('b', 64)
        };
        await fixture.Webhooks.Process("signed-test", default);
        Assert.True((await fixture.Service.GetById(first.Payment.PaymentId, default)).RequiresReview);
        Assert.Equal(2, await fixture.Core.Db.PaymentWebhooks.CountAsync());
    }

    [Fact]
    public async Task SameRequestKeyCannotBeReboundToAnotherCheckoutOfTheSameBill()
    {
        await using var fixture = await Fixture.Create();
        var first = await fixture.CreatePayment();
        await fixture.Service.Cancel(first.Payment.PaymentId, default);
        var prior = await fixture.Core.Db.BillCheckouts.SingleAsync();
        prior.Status = "Released";
        prior.ReleasedAt = DateTime.UtcNow;
        var newId = Guid.NewGuid();
        fixture.Core.Db.BillCheckouts.Add(new BillCheckout
        {
            CheckoutId = newId,
            BillId = prior.BillId,
            TableSessionRef = prior.TableSessionRef,
            SourceHash = prior.SourceHash,
            Status = "Ready",
            CreatedAt = DateTime.UtcNow
        });
        await fixture.Core.Db.SaveChangesAsync();
        await Status(409, () => fixture.Service.Create(fixture.Core.Bill.BillId, fixture.Core.Key, new CreatePayOsPaymentDTO
        {
            CheckoutId = newId,
            CashShiftId = fixture.Core.ShiftId
        }, default));
        Assert.Single(await fixture.Core.Db.Payments.ToListAsync());
    }

    [Fact]
    public async Task MismatchedProviderRecordIsReviewedWithoutCancellingIt()
    {
        await using var fixture = await Fixture.Create();
        var first = await fixture.CreatePayment();
        var row = await fixture.Core.Db.Payments.SingleAsync();
        row.ExpiredAt = DateTime.UtcNow.AddMinutes(-1);
        await fixture.Core.Db.SaveChangesAsync();
        fixture.Provider.Value = fixture.Provider.Value! with
        {
            LinkId = "another-link"
        };
        var result = await fixture.Service.Reconcile(first.Payment.PaymentId, default);
        Assert.True(result.RequiresReview);
        Assert.Equal(0, fixture.Provider.CancelCalls);
    }

    private static async Task Status(int code, Func<Task> action)
    {
        Assert.Equal(code, (await Assert.ThrowsAsync<BillingException>(action)).StatusCode);
    }

    internal sealed class Fixture : IAsyncDisposable
    {
        public CashPaymentServiceTests.Fixture Core { get; }
        public FakeProvider Provider { get; } = new();
        public PayOsSettings Settings { get; } = new()
        {
            Enabled = true
        };
        public PayOsPaymentService Service { get; }
        public PaymentWebhookService Webhooks { get; }

        private Fixture(CashPaymentServiceTests.Fixture core)
        {
            Core = core;
            var db = core.Db;
            var payments = new PaymentRepository(db);
            var bills = new BillRepository(db);
            var shifts = new CashShiftRepository(db);
            var registers = new CashRegisterRepository(db);
            var webhooks = new PaymentWebhookRepository(db);
            var uow = new UnitOfWork<BillingDbContext>(db);
            Service = new PayOsPaymentService(payments, bills, new BillCheckoutRepository(db), shifts, registers,
                new PaymentMethodRepository(db), webhooks, uow, core.Core.Actor, core.Core.Service, core.Remote,
                Provider, Options.Create(new CashRegisterAccessSettings
                {
                    WaiterRoleIds = ["waiter"],
                    CashierRoleIds = ["cashier"],
                    ManagerRoleIds = ["manager"]
                }), Options.Create(Settings), TimeProvider.System, new TestPaymentRequestClient
                {
                    ReviewedWaiterId = core.WaiterId
                });
            Webhooks = new PaymentWebhookService(payments, webhooks, bills, registers, shifts, uow, Provider, Service, TimeProvider.System);
        }

        public static async Task<Fixture> Create()
        {
            var fixture = new Fixture(await CashPaymentServiceTests.Fixture.Create());
            fixture.Core.Db.PaymentMethods.Add(new PaymentMethod
            {
                MethodCode = "BankTransfer",
                IsEnabled = true
            });
            await fixture.Core.Db.SaveChangesAsync();
            return fixture;
        }

        public Task<(PayOsPaymentDTO Payment, bool Created)> CreatePayment(string? key = null)
        {
            return Service.Create(Core.Bill.BillId, key ?? Core.Key, new CreatePayOsPaymentDTO
            {
                CheckoutId = Core.CheckoutId,
                CashShiftId = Core.ShiftId
            }, default);
        }

        public PayOsWebhookDTO Event(long amount)
        {
            return new PayOsWebhookDTO(Provider.Value!.OrderCode, amount, "BANK-001", "link-001", "VND", "00", new string('a', 64), new string('c', 64));
        }

        public ValueTask DisposeAsync()
        {
            return Core.DisposeAsync();
        }
    }

    internal sealed class FakeProvider : IPayOsClient
    {
        public bool IsConfigured => true;
        public bool Offline { get; set; }
        public bool CancelTimeout { get; set; }
        public int CreateCalls { get; private set; }
        public int GetCalls { get; private set; }
        public int CancelCalls { get; private set; }
        public PayOsProviderResult? Value { get; set; }
        public PayOsWebhookDTO? Webhook { get; set; }

        public void EnsureConfigured()
        {
        }

        public void EnsureCanCreate()
        {
        }

        public Task<PayOsProviderResult> CreateAsync(PayOsCreateCommand request, CancellationToken ct)
        {
            CreateCalls++;
            if (Offline)
            {
                throw new PayOsClientException("OutcomeUnknown");
            }

            Value ??= new PayOsProviderResult(request.OrderCode, request.Amount, "PENDING", "link-001", 0, request.Amount, [],
                "https://pay.example/link", "real-provider-test-qr");
            return Task.FromResult(Value);
        }

        public Task<PayOsProviderResult> GetAsync(long orderCode, CancellationToken ct)
        {
            GetCalls++;
            if (Offline || Value is null)
            {
                throw new PayOsClientException("OutcomeUnknown");
            }

            return Task.FromResult(Value);
        }

        public Task<PayOsProviderResult> CancelAsync(long orderCode, string reason, CancellationToken ct)
        {
            CancelCalls++;
            if (CancelTimeout || Offline || Value is null)
            {
                throw new PayOsClientException("OutcomeUnknown");
            }

            Value = Value with
            {
                Status = "CANCELLED"
            };
            return Task.FromResult(Value);
        }

        public void Settle(long amount)
        {
            Value = Value! with
            {
                AmountPaid = amount,
                AmountRemaining = Value!.Amount - amount,
                Status = "PAID",
                Transactions = [new PayOsTransactionDTO("BANK-001", amount)]
            };
        }

        public PayOsWebhookDTO VerifyWebhook(string payload)
        {
            return Webhook ?? throw new BillingException(400, "Invalid test signature.");
        }
    }
}

