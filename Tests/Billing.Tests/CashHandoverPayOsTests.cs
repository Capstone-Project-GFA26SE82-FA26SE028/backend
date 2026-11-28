using Billing.BLL.Implements;
using Billing.BLL.Interfaces;
using Billing.Common.Constants;
using Billing.Common.DTOs;
using Billing.Common.Exceptions;
using Billing.Common.Settings;
using Billing.DAL.Implements;
using Billing.DAL.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Shared.Persistence.UnitOfWorks;

namespace Billing.Tests;

public sealed class CashHandoverPayOsTests
{
    [Fact]
    public async Task WaiterReportsHandoverWhileQrActiveAndCashierConfirmAutoCancelsQrAndSettlesCash()
    {
        await using var fixture = await Fixture.Create();
        var qr = await fixture.CreateQr();
        Assert.Equal("Pending", qr.Status);

        var (handover, created) = await fixture.ReportHandover(300m);
        Assert.True(created);
        Assert.Equal(CashHandoverStatuses.Pending, handover.Status);
        Assert.Equal(CashHandoverStatuses.PendingPayment, handover.PaymentStatus);
        Assert.Equal("Unpaid", (await fixture.Db.Bills.AsNoTracking().SingleAsync()).Status);
        Assert.Empty(await fixture.Db.CashMovements.AsNoTracking().ToListAsync());

        var confirmed = await fixture.ConfirmHandover(handover.PaymentId, 300m);
        Assert.Equal(CashHandoverStatuses.Confirmed, confirmed.Status);
        Assert.Equal(PaymentStatuses.Paid, confirmed.PaymentStatus);
        Assert.Equal(300m, confirmed.ConfirmedAmount);
        Assert.Equal(68m, confirmed.ChangeAmount);
        Assert.Equal(PaymentStatuses.Paid, (await fixture.Db.Bills.AsNoTracking().SingleAsync()).Status);
        Assert.Equal(PaymentStatuses.Paid, (await fixture.Db.BillCheckouts.AsNoTracking().SingleAsync()).Status);
        Assert.Equal(1, fixture.Provider.CancelCalls);

        var payOsAfter = await fixture.PayOs.GetById(qr.PaymentId, default);
        Assert.True(payOsAfter.IsVerifiedZero);
        Assert.Equal("Cancelled", payOsAfter.Status);
        Assert.Single(await fixture.Db.CashMovements.AsNoTracking().ToListAsync());

        var retry = await fixture.ConfirmHandover(handover.PaymentId, 300m);
        Assert.Equal(confirmed.PaymentId, retry.PaymentId);
        Assert.Equal(confirmed.ConfirmedAt, retry.ConfirmedAt);
        Assert.Single(await fixture.Db.CashMovements.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task CashierConfirmDetectsPayOsPaidWhenCancelIsRejectedAndReturnsCashReturnRequiredEvenAfterShiftClose()
    {
        await using var fixture = await Fixture.Create();
        var qr = await fixture.CreateQr();
        var (handover, _) = await fixture.ReportHandover(300m);

        // Simulate customer paying right between GetAsync (PENDING) and CancelAsync.
        fixture.Provider.SettleAndRejectCancelAmount = 232;

        var error = await Assert.ThrowsAsync<BillingException>(() => fixture.ConfirmHandover(handover.PaymentId, 300m));
        Assert.Equal(409, error.StatusCode);
        Assert.Equal("CashReturnRequired", error.Code);
        Assert.Equal(300m, error.Amount);

        var bill = await fixture.Db.Bills.AsNoTracking().SingleAsync();
        Assert.Equal(PaymentStatuses.Paid, bill.Status);
        var payOsAfter = await fixture.PayOs.GetById(qr.PaymentId, default);
        Assert.Equal(PaymentStatuses.Paid, payOsAfter.Status);
        var storedHandover = await fixture.GetHandover(handover.PaymentId);
        Assert.Equal(CashHandoverStatuses.ReturnRequired, storedHandover.Status);
        Assert.Equal(CashHandoverStatuses.SupersededByPayOs, storedHandover.PaymentStatus);
        Assert.Empty(await fixture.Db.CashMovements.AsNoTracking().ToListAsync());

        // Even after shift close, retrying confirm must still return CashReturnRequired.
        var shift = await fixture.Db.CashShifts.SingleAsync();
        shift.Status = CashShiftStatuses.Closed;
        shift.ClosedAt = DateTime.UtcNow;
        await fixture.Db.SaveChangesAsync();

        var retryError = await Assert.ThrowsAsync<BillingException>(() => fixture.ConfirmHandover(handover.PaymentId, 300m));
        Assert.Equal(409, retryError.StatusCode);
        Assert.Equal("CashReturnRequired", retryError.Code);
        Assert.Equal(300m, retryError.Amount);
    }

    [Fact]
    public async Task CashierConfirmReturnsPendingReconciliationWhenPayOsUnavailableAndSucceedsOnRetry()
    {
        await using var fixture = await Fixture.Create();
        await fixture.CreateQr();
        var (handover, _) = await fixture.ReportHandover(300m);

        fixture.Provider.Offline = true;
        var pending = await Assert.ThrowsAsync<BillingException>(() => fixture.ConfirmHandover(handover.PaymentId, 300m));
        Assert.Equal(409, pending.StatusCode);
        Assert.Equal("PendingReconciliation", pending.Code);
        Assert.Equal("Unpaid", (await fixture.Db.Bills.AsNoTracking().SingleAsync()).Status);
        Assert.Equal(CashHandoverStatuses.Pending, (await fixture.GetHandover(handover.PaymentId)).Status);
        Assert.Empty(await fixture.Db.CashMovements.AsNoTracking().ToListAsync());

        fixture.Provider.Offline = false;
        var confirmed = await fixture.ConfirmHandover(handover.PaymentId, 300m);
        Assert.Equal(CashHandoverStatuses.Confirmed, confirmed.Status);
        Assert.Equal(PaymentStatuses.Paid, (await fixture.Db.Bills.AsNoTracking().SingleAsync()).Status);
        Assert.Single(await fixture.Db.CashMovements.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task WebhookSettlingPayOsMarksHandoverReturnRequiredAndBlocksCashConfirmation()
    {
        await using var fixture = await Fixture.Create();
        var qr = await fixture.CreateQr();
        var (handover, _) = await fixture.ReportHandover(300m);

        fixture.Provider.Settle(232);
        fixture.Provider.Webhook = fixture.WebhookEvent(232);
        await fixture.Webhooks.Process("{\"code\":\"00\"}", default);

        var error = await Assert.ThrowsAsync<BillingException>(() => fixture.ConfirmHandover(handover.PaymentId, 300m));
        Assert.Equal(409, error.StatusCode);
        Assert.Equal("CashReturnRequired", error.Code);
        Assert.Equal(300m, error.Amount);

        var paidPayments = await fixture.Db.Payments.AsNoTracking()
            .Where(value => value.Status == PaymentStatuses.Paid)
            .ToListAsync();
        Assert.Equal(qr.PaymentId, Assert.Single(paidPayments).PaymentId);
        Assert.Equal(CashHandoverStatuses.ReturnRequired, (await fixture.GetHandover(handover.PaymentId)).Status);
        Assert.Empty(await fixture.Db.CashMovements.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task WaiterHandoverAfterPayOsSettledReturnsCashReturnRequired()
    {
        await using var fixture = await Fixture.Create();
        var qr = await fixture.CreateQr();
        fixture.Provider.Settle(232);
        await fixture.PayOs.Reconcile(qr.PaymentId, default);

        var error = await Assert.ThrowsAsync<BillingException>(() => fixture.ReportHandover(300m));
        Assert.Equal(409, error.StatusCode);
        Assert.Equal("CashReturnRequired", error.Code);
        Assert.Equal(300m, error.Amount);
        Assert.Empty(await fixture.Db.CashHandovers.AsNoTracking().ToListAsync());
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly CashPaymentServiceTests.Fixture _core;
        private readonly BillingAccessScope _cashierScope;
        private readonly BillingAccessScope _waiterScope;
        public BillingDbContext Db => _core.Db;
        public FakeProvider Provider { get; } = new();
        public PayOsPaymentService PayOs { get; }
        public PaymentWebhookService Webhooks { get; }
        public PaymentService Payments { get; }
        public CashHandoverService Handovers { get; }

        private Fixture(CashPaymentServiceTests.Fixture core)
        {
            _core = core;
            _cashierScope = core.Core.Actor.Scope;
            _waiterScope = new BillingAccessScope
            {
                UserId = Guid.NewGuid(),
                RoleId = "waiter",
                RestaurantIds = [core.Bill.RestaurantId]
            };

            var db = core.Db;
            var paymentsRepo = new PaymentRepository(db);
            var billsRepo = new BillRepository(db);
            var checkoutsRepo = new BillCheckoutRepository(db);
            var shiftsRepo = new CashShiftRepository(db);
            var registersRepo = new CashRegisterRepository(db);
            var methodsRepo = new PaymentMethodRepository(db);
            var webhooksRepo = new PaymentWebhookRepository(db);
            var handoversRepo = new CashHandoverRepository(db);
            var movementsRepo = new CashMovementRepository(db);
            var uow = new UnitOfWork<BillingDbContext>(db);
            var accessRoles = Options.Create(new CashRegisterAccessSettings
            {
                CashierRoleIds = ["cashier"],
                ManagerRoleIds = ["manager"],
                WaiterRoleIds = ["waiter"]
            });
            var cashSettings = Options.Create(new CashPaymentSettings
            {
                MethodCode = "Cash"
            });
            var payOsSettings = Options.Create(new PayOsSettings
            {
                Enabled = true
            });

            PayOs = new PayOsPaymentService(paymentsRepo, billsRepo, checkoutsRepo, shiftsRepo, registersRepo,
                methodsRepo, webhooksRepo, uow, core.Core.Actor, core.Core.Service, core.Remote, Provider,
                accessRoles, payOsSettings, TimeProvider.System, new TestPaymentReviewClient(), new TestPaidSessionClosureClient());
            Webhooks = new PaymentWebhookService(paymentsRepo, webhooksRepo, billsRepo, registersRepo, shiftsRepo,
                uow, Provider, PayOs, TimeProvider.System);
            Payments = new PaymentService(paymentsRepo, billsRepo, checkoutsRepo, shiftsRepo, registersRepo,
                movementsRepo, uow, core.Core.Actor, core.Core.Service, core.Remote, accessRoles, cashSettings,
                TimeProvider.System, payOs: PayOs, methods: methodsRepo, closure: new TestPaidSessionClosureClient());
            Handovers = new CashHandoverService(handoversRepo, paymentsRepo, billsRepo, checkoutsRepo, registersRepo,
                shiftsRepo, methodsRepo, uow, core.Core.Actor, new PresentedReview(core.Bill.RestaurantId), Payments,
                accessRoles, cashSettings, TimeProvider.System, new TestPaidSessionClosureClient());
        }

        public static async Task<Fixture> Create()
        {
            var core = await CashPaymentServiceTests.Fixture.Create();
            var shift = await core.Db.CashShifts.SingleAsync();
            var checkout = await core.Db.BillCheckouts.SingleAsync();
            var now = DateTime.UtcNow;
            checkout.Status = "Presented";
            checkout.CashRegisterId = shift.CashRegisterId;
            checkout.SubmittedAt = now;
            checkout.PreparedByRef = shift.CashierUserRef;
            checkout.PreparedAt = now;
            checkout.PresentedAt = now;
            core.Db.PaymentMethods.Add(new PaymentMethod
            {
                MethodCode = "BankTransfer",
                IsEnabled = true
            });
            await core.Db.SaveChangesAsync();
            return new Fixture(core);
        }

        public async Task<PayOsPaymentDTO> CreateQr()
        {
            _core.Core.Actor.Scope = _cashierScope;
            var (payment, _) = await PayOs.Create(_core.Bill.BillId, Guid.NewGuid().ToString(), new CreatePayOsPaymentDTO
            {
                CheckoutId = _core.CheckoutId,
                CashShiftId = _core.ShiftId
            }, default);
            return payment;
        }

        public Task<(CashHandoverDTO Handover, bool Created)> ReportHandover(decimal received, string? key = null)
        {
            _core.Core.Actor.Scope = _waiterScope;
            return Handovers.Create(_core.Bill.BillId, key ?? Guid.NewGuid().ToString(), new CreateCashHandoverDTO
            {
                CheckoutId = _core.CheckoutId,
                CashReceived = received
            }, default);
        }

        public Task<CashHandoverDTO> ConfirmHandover(Guid paymentId, decimal confirmedAmount)
        {
            _core.Core.Actor.Scope = _cashierScope;
            return Handovers.Confirm(paymentId, confirmedAmount, default);
        }

        public Task<CashHandoverDTO> GetHandover(Guid paymentId)
        {
            _core.Core.Actor.Scope = _cashierScope;
            return Handovers.GetById(paymentId, default);
        }

        public PayOsWebhookDTO WebhookEvent(long amount)
        {
            return new PayOsWebhookDTO(Provider.Value!.OrderCode, amount, "BANK-001", "link-001", "VND", "00",
                new string('a', 64), new string('c', 64));
        }

        public ValueTask DisposeAsync()
        {
            return _core.DisposeAsync();
        }
    }

    private sealed class PresentedReview(Guid restaurantId) : IPaymentReviewClient
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

    private sealed class FakeProvider : IPayOsClient
    {
        public bool IsConfigured => true;
        public bool Offline { get; set; }
        public long? SettleAndRejectCancelAmount { get; set; }
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
            if (Offline || Value is null)
            {
                throw new PayOsClientException("OutcomeUnknown");
            }

            return Task.FromResult(Value);
        }

        public Task<PayOsProviderResult> CancelAsync(long orderCode, string reason, CancellationToken ct)
        {
            CancelCalls++;
            if (SettleAndRejectCancelAmount.HasValue && Value is not null)
            {
                Settle(SettleAndRejectCancelAmount.Value);
                SettleAndRejectCancelAmount = null;
                throw new PayOsClientException("ProviderRejected", "200");
            }

            if (Offline || Value is null)
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
