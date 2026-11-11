using Billing.Common.DTOs;
using Billing.Common.Exceptions;
using Billing.DAL.Models;
using Microsoft.EntityFrameworkCore;

namespace Billing.Tests;

public sealed class PayOsWaiterTests
{
    [Fact]
    public async Task ResponsibleWaiterReadsStoredQrAndPriceWithoutBankCallsOrManagementPermission()
    {
        await using var f = await PayOsPaymentServiceTests.Fixture.Create();
        var created = await f.CreatePayment();
        AsWaiter(f);
        var calls = f.Provider.GetCalls;
        f.Provider.Offline = true;
        var value = await Read(f, created.Payment.PaymentId);
        Assert.True(value.CanPresentQr);
        Assert.Equal("Pending", value.Status);
        Assert.Equal("Unpaid", value.BillStatus);
        Assert.Equal(232, value.Amount);
        Assert.Equal("https://pay.example/link", value.CheckoutUrl);
        Assert.Equal("real-provider-test-qr", value.QrCode);
        Assert.False(value.PaymentConfirmed);
        Assert.False(value.RequiresReview);
        var page = await f.Service.GetForCheckout(f.Core.Core.Source.Value.TableSessionId, f.Core.CheckoutId, 1, 20, default);
        Assert.Equal(value.PaymentId, Assert.Single(page.Items).PaymentId);
        Assert.Equal(1, f.Provider.CreateCalls);
        Assert.Equal(0, f.Provider.CancelCalls);
        Assert.Equal(calls, f.Provider.GetCalls);
        await Error(403, () => f.Service.GetById(value.PaymentId, default));
        await Error(403, () => f.Service.Reconcile(value.PaymentId, default));
        await Error(403, () => f.Service.Cancel(value.PaymentId, default));
        await Error(403, () => f.CreatePayment(Guid.NewGuid().ToString()));
        Assert.Single(await f.Core.Db.Payments.ToListAsync());
        Assert.Equal("Unpaid", (await f.Core.Db.Bills.SingleAsync()).Status);
        Assert.Empty(await f.Core.Db.CashMovements.ToListAsync());
    }

    [Theory]
    [InlineData("Unknown")]
    [InlineData("CancelRequested")]
    [InlineData("Creating")]
    [InlineData("ReviewRequired")]
    [InlineData("Cancelled")]
    [InlineData("Expired")]
    [InlineData("Failed")]
    public async Task NonPayableStatusesNeverExposeStoredQr(string status)
    {
        await using var f = await PayOsPaymentServiceTests.Fixture.Create();
        var created = await f.CreatePayment();
        var payment = await f.Core.Db.Payments.SingleAsync();
        payment.Status = status;
        await f.Core.Db.SaveChangesAsync();
        AsWaiter(f);
        var value = await Read(f, created.Payment.PaymentId);
        Assert.Equal(status, value.Status);
        Assert.False(value.CanPresentQr);
        Assert.Null(value.QrCode);
        Assert.Null(value.CheckoutUrl);
        Assert.False(value.PaymentConfirmed);
    }

    [Fact]
    public async Task LocalExpiryMissingLinkWebhookClaimAndClosedShiftHideQrWithoutInventingFailure()
    {
        await using var f = await PayOsPaymentServiceTests.Fixture.Create();
        var created = await f.CreatePayment();
        AsWaiter(f);
        var payment = await f.Core.Db.Payments.SingleAsync();
        payment.ExpiredAt = DateTime.UtcNow.AddSeconds(-1);
        await f.Core.Db.SaveChangesAsync();
        var expired = await Read(f, created.Payment.PaymentId);
        Assert.False(expired.CanPresentQr);
        Assert.Equal("Pending", expired.Status);
        payment.ExpiredAt = DateTime.UtcNow.AddMinutes(15);
        payment.ProviderQrCode = null;
        await f.Core.Db.SaveChangesAsync();
        Assert.False((await Read(f, created.Payment.PaymentId)).CanPresentQr);
        payment.ProviderQrCode = "stored-qr";
        payment.ProviderTransactionId = "unverified-webhook-claim";
        payment.VerificationSource = "PayOSWebhook";
        await f.Core.Db.SaveChangesAsync();
        var claimed = await Read(f, created.Payment.PaymentId);
        Assert.False(claimed.CanPresentQr);
        Assert.False(claimed.PaymentConfirmed);
        payment.ProviderTransactionId = null;
        payment.VerificationSource = null;
        (await f.Core.Db.CashShifts.SingleAsync()).ClosedAt = DateTime.UtcNow;
        await f.Core.Db.SaveChangesAsync();
        Assert.False((await Read(f, created.Payment.PaymentId)).CanPresentQr);
        Assert.Equal("Unpaid", (await f.Core.Db.Bills.SingleAsync()).Status);
    }

    [Fact]
    public async Task PaidOnlyAfterProviderVerificationAndReadNeverCreatesSaleOrReleasesCheckout()
    {
        await using var f = await PayOsPaymentServiceTests.Fixture.Create();
        var created = await f.CreatePayment();
        f.Provider.Settle(232);
        await f.Service.Reconcile(created.Payment.PaymentId, default);
        AsWaiter(f);
        var value = await Read(f, created.Payment.PaymentId);
        Assert.True(value.PaymentConfirmed);
        Assert.Equal("Paid", value.Status);
        Assert.Equal("Paid", value.BillStatus);
        Assert.NotNull(value.PaidAt);
        Assert.False(value.CanPresentQr);
        Assert.Null(value.QrCode);
        Assert.Null(value.CheckoutUrl);
        Assert.Null((await f.Core.Db.BillCheckouts.SingleAsync()).ReleasedAt);
        Assert.Empty(await f.Core.Db.CashMovements.ToListAsync());
    }

    [Fact]
    public async Task OtherWaiterRevokedAssignmentAndMismatchedSessionCannotReadPayment()
    {
        await using var f = await PayOsPaymentServiceTests.Fixture.Create();
        var created = await f.CreatePayment();
        AsWaiter(f);
        f.Core.Core.Actor.Scope.UserId = Guid.NewGuid();
        await Error(403, () => Read(f, created.Payment.PaymentId));
        AsWaiter(f);
        f.Core.Core.Actor.Scope.RestaurantIds = [];
        await Error(403, () => Read(f, created.Payment.PaymentId));
        f.Core.Core.Actor.Scope.RestaurantIds = [f.Core.Bill.RestaurantId];
        await Error(404, () => f.Service.GetForWaiter(Guid.NewGuid(), f.Core.CheckoutId, created.Payment.PaymentId, default));
        var payment = await f.Core.Db.Payments.SingleAsync();
        payment.IdempotencyKey = "PayOS:" + Guid.NewGuid().ToString("N") + ":" + Guid.NewGuid().ToString("N");
        await f.Core.Db.SaveChangesAsync();
        await Error(404, () => Read(f, created.Payment.PaymentId));
        Assert.Empty((await f.Service.GetForCheckout(f.Core.Core.Source.Value.TableSessionId,
            f.Core.CheckoutId, 1, 20, default)).Items);
    }

    [Fact]
    public async Task ConfirmedZeroAttemptDoesNotHideReplacementButAmbiguousBlockingPaymentsHideBoth()
    {
        await using var f = await PayOsPaymentServiceTests.Fixture.Create();
        var current = await f.CreatePayment();
        var old = new Payment
        {
            PaymentId = Guid.NewGuid(),
            BillId = f.Core.Bill.BillId,
            CashShiftId = f.Core.ShiftId,
            MethodCode = "BankTransfer",
            Status = "Cancelled",
            VerificationSource = "PayOSZero",
            Amount = 232,
            IdempotencyKey = "PayOS:" + Guid.NewGuid().ToString("N") + ":" + f.Core.CheckoutId.ToString("N"),
            ProviderOrderCode = "123456789",
            ProviderPaymentLinkId = "old-link",
            ProviderCheckoutUrl = "https://pay.example/old-link",
            ProviderQrCode = "old-qr",
            ExpiredAt = DateTime.UtcNow.AddMinutes(-1)
        };
        f.Core.Db.Payments.Add(old);
        await f.Core.Db.SaveChangesAsync();
        AsWaiter(f);
        var page = await f.Service.GetForCheckout(f.Core.Core.Source.Value.TableSessionId, f.Core.CheckoutId, 1, 20, default);
        Assert.Equal(2, page.TotalCount);
        Assert.True(page.Items.Single(value => value.PaymentId == current.Payment.PaymentId).CanPresentQr);
        Assert.False(page.Items.Single(value => value.PaymentId == old.PaymentId).RequiresReview);
        old.Status = "Pending";
        await f.Core.Db.SaveChangesAsync();
        page = await f.Service.GetForCheckout(f.Core.Core.Source.Value.TableSessionId, f.Core.CheckoutId, 1, 20, default);
        Assert.All(page.Items, value =>
        {
            Assert.False(value.CanPresentQr);
            Assert.Null(value.QrCode);
            Assert.True(value.RequiresReview);
        });
    }

    [Fact]
    public async Task PresentedAndFinancialHashAreRequiredEvenWhenPaymentRecordHasQr()
    {
        await using var f = await PayOsPaymentServiceTests.Fixture.Create();
        var created = await f.CreatePayment();
        AsWaiter(f);
        var checkout = await f.Core.Db.BillCheckouts.SingleAsync();
        checkout.PresentedAt = null;
        await f.Core.Db.SaveChangesAsync();
        await Error(409, () => Read(f, created.Payment.PaymentId));
        checkout.PresentedAt = DateTime.UtcNow;
        checkout.SourceHash = new string('F', 64);
        await f.Core.Db.SaveChangesAsync();
        await Error(409, () => Read(f, created.Payment.PaymentId));
    }

    [Fact]
    public async Task LateExtraMoneyDisplaysReviewWithoutRevertingPaidBillOrShowingQr()
    {
        await using var f = await PayOsPaymentServiceTests.Fixture.Create();
        var created = await f.CreatePayment();
        f.Provider.Settle(232);
        await f.Service.Reconcile(created.Payment.PaymentId, default);
        f.Provider.Value = f.Provider.Value! with
        {
            AmountPaid = 242,
            AmountRemaining = -10,
            Transactions = [new PayOsTransactionDTO("BANK-001", 232), new PayOsTransactionDTO("BANK-002", 10)]
        };
        await f.Service.Reconcile(created.Payment.PaymentId, default);
        AsWaiter(f);
        var value = await Read(f, created.Payment.PaymentId);
        Assert.Equal("ReviewRequired", value.Status);
        Assert.Equal("Paid", value.BillStatus);
        Assert.True(value.RequiresReview);
        Assert.False(value.PaymentConfirmed);
        Assert.False(value.CanPresentQr);
        Assert.Null(value.QrCode);
        Assert.Single(await f.Core.Db.Payments.ToListAsync());
        Assert.Empty(await f.Core.Db.CashMovements.ToListAsync());
    }

    [Fact]
    public async Task PaymentIdFromAnotherBillIsNotExposedThroughOwnedCheckout()
    {
        await using var f = await PayOsPaymentServiceTests.Fixture.Create();
        await f.CreatePayment();
        var other = new Payment
        {
            PaymentId = Guid.NewGuid(),
            Bill = new Bill
            {
                BillId = Guid.NewGuid(),
                RestaurantId = Guid.NewGuid(),
                BillNumber = "OTHER-SCOPE",
                Currency = "VND",
                Status = "Unpaid",
                TotalAmount = 232,
                SubTotal = 232
            },
            CashShift = new CashShift
            {
                CashShiftId = Guid.NewGuid(),
                CashRegister = new CashRegister
                {
                    CashRegisterId = Guid.NewGuid(),
                    RestaurantId = Guid.NewGuid(),
                    Code = "OTHER"
                },
                CashierUserRef = Guid.NewGuid(),
                Status = "Active",
                OpenedAt = DateTime.UtcNow
            },
            MethodCode = "BankTransfer",
            Amount = 232,
            Status = "Pending",
            ProviderOrderCode = "234567890",
            IdempotencyKey = "PayOS:" + Guid.NewGuid().ToString("N") + ":" + Guid.NewGuid().ToString("N")
        };
        other.CashShift.CashRegister.RestaurantId = other.Bill.RestaurantId;
        f.Core.Db.Payments.Add(other);
        await f.Core.Db.SaveChangesAsync();
        AsWaiter(f);
        await Error(404, () => Read(f, other.PaymentId));
        Assert.Single((await f.Service.GetForCheckout(f.Core.Core.Source.Value.TableSessionId,
            f.Core.CheckoutId, 1, 20, default)).Items);
    }

    [Fact]
    public async Task PaidFlagWithoutVerifiedReceiptIsMarkedForReview()
    {
        await using var f = await PayOsPaymentServiceTests.Fixture.Create();
        var created = await f.CreatePayment();
        (await f.Core.Db.Payments.SingleAsync()).Status = "Paid";
        await f.Core.Db.SaveChangesAsync();
        AsWaiter(f);
        var value = await Read(f, created.Payment.PaymentId);
        Assert.False(value.PaymentConfirmed);
        Assert.True(value.RequiresReview);
        Assert.False(value.CanPresentQr);
        Assert.Null(value.QrCode);
        Assert.Equal("Unpaid", value.BillStatus);
    }

    private static Task<WaiterPayOsPaymentDTO> Read(PayOsPaymentServiceTests.Fixture f, Guid paymentId)
    {
        return f.Service.GetForWaiter(f.Core.Core.Source.Value.TableSessionId, f.Core.CheckoutId, paymentId, default);
    }

    private static void AsWaiter(PayOsPaymentServiceTests.Fixture f)
    {
        f.Core.Core.Actor.Scope.RoleId = "waiter";
        f.Core.Core.Actor.Scope.UserId = f.Core.WaiterId;
    }

    private static async Task Error(int status, Func<Task> action)
    {
        Assert.Equal(status, (await Assert.ThrowsAsync<BillingException>(action)).StatusCode);
    }
}
