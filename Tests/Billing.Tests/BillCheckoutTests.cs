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

public sealed class BillCheckoutTests
{
    [Fact]
    public void FinancialHashIgnoresDecimalScaleLineOrderAndZeroQuantityHistory()
    {
        var id = Guid.NewGuid();
        var item = Guid.NewGuid();
        var first = new CheckoutHashLine(id, item, 2m, 100.00m, []);
        var equivalent = new CheckoutHashLine(id, item, 2.00m, 100m, []);
        Assert.Equal(CheckoutHash.Compute([first]), CheckoutHash.Compute([
            new CheckoutHashLine(Guid.NewGuid(), Guid.NewGuid(), 0, 999, []), equivalent]));
        Assert.NotEqual(CheckoutHash.Compute([first]), CheckoutHash.Compute([equivalent with { Quantity = 3 }]));
    }

    [Fact]
    public async Task WaitingCheckoutDoesNotCreateBillAndSynchronizesAllConfirmedItemsLater()
    {
        await using var fixture = await BillServiceTests.BillFixture.Create();
        var remote = new Remote(fixture.Source);
        var service = Service(fixture, remote);
        fixture.Source.Value.PendingOrderCount = 1;
        var id = Guid.NewGuid();
        var requested = await service.Request(fixture.Source.Value.TableSessionId, id, "qr", default);
        Assert.Equal("WaitingConfirmation", requested.Status);
        Assert.Null(requested.BillId);
        Assert.Empty(await fixture.Db.Bills.ToListAsync());
        Assert.Equal("WaitingConfirmation", (await service.Synchronize(requested.TableSessionId, id, default)).Status);
        fixture.Source.Value.PendingOrderCount = 0;
        fixture.Source.Value.Items[0].Quantity = 3;
        var ready = await service.Synchronize(requested.TableSessionId, id, default);
        Assert.Equal("Ready", ready.Status);
        Assert.Equal(603.75m, (await fixture.Db.Bills.SingleAsync()).SubTotal);
        Assert.False(ready.OrderingReopened);
        Assert.Equal(ready.CheckoutId, (await service.GetCurrent(requested.TableSessionId, "second-device", default)).CheckoutId);
        await service.Synchronize(requested.TableSessionId, id, default);
        Assert.Single(await fixture.Db.Bills.ToListAsync());
        Assert.Single(await fixture.Db.BillLines.ToListAsync());
    }

    [Fact]
    public async Task ExistingBillIsRecalculatedBeforeReadyAndCannotBeRefreshedOutsideCheckout()
    {
        await using var fixture = await BillServiceTests.BillFixture.Create();
        var original = await fixture.Service.Create(fixture.Source.Value.TableSessionId, default);
        fixture.Source.Value.Items[0].Quantity = 2;
        var service = Service(fixture, new Remote(fixture.Source));
        var state = await service.Request(fixture.Source.Value.TableSessionId, Guid.NewGuid(), "qr", default);
        await Error(409, () => fixture.Service.Refresh(original.BillId, default));
        var ready = await service.Synchronize(state.TableSessionId, state.CheckoutId, default);
        Assert.Equal(original.BillId, ready.BillId);
        Assert.Equal(402.5m, (await fixture.Db.Bills.SingleAsync()).SubTotal);
        Assert.Equal(original.VatRateSnapshot, (await fixture.Db.Bills.SingleAsync()).VatRateSnapshot);
    }

    [Fact]
    public async Task LostBeginReplyAndNewServiceInstanceResumeSamePersistedCheckout()
    {
        await using var fixture = await BillServiceTests.BillFixture.Create();
        var remote = new Remote(fixture.Source)
        {
            LoseBeginReply = true
        };
        var id = Guid.NewGuid();
        await Error(503, () => Service(fixture, remote).Request(fixture.Source.Value.TableSessionId, id, "qr", default));
        Assert.Contains(id, remote.HeldCheckouts);
        Assert.Equal("Preparing", (await fixture.Db.BillCheckouts.SingleAsync()).Status);
        remote.LoseBeginReply = false;
        var resumed = await Service(fixture, remote).Request(fixture.Source.Value.TableSessionId, id, "qr", default);
        Assert.Equal("Frozen", resumed.Status);
        Assert.Single(await fixture.Db.BillCheckouts.ToListAsync());
        Assert.False(resumed.OrderingReopened);
    }

    [Fact]
    public async Task LostBindReplyResumesExistingBillInsteadOfCreatingAnotherBill()
    {
        await using var fixture = await BillServiceTests.BillFixture.Create();
        var remote = new Remote(fixture.Source)
        {
            LoseBindReply = true
        };
        var service = Service(fixture, remote);
        var state = await service.Request(fixture.Source.Value.TableSessionId, Guid.NewGuid(), "qr", default);
        await Error(503, () => service.Synchronize(state.TableSessionId, state.CheckoutId, default));
        Assert.Single(await fixture.Db.Bills.ToListAsync());
        Assert.Equal("Frozen", (await service.GetCurrent(state.TableSessionId, "qr", default)).Status);
        remote.LoseBindReply = false;
        var resumed = await Service(fixture, remote).Synchronize(state.TableSessionId, state.CheckoutId, default);
        Assert.Equal("Ready", resumed.Status);
        Assert.Single(await fixture.Db.Bills.ToListAsync());
        Assert.Single(await fixture.Db.BillLines.ToListAsync());
    }

    [Fact]
    public async Task MissingTaxPolicyLeavesCheckoutFrozenWithoutFakeBillOrReadyState()
    {
        await using var fixture = await BillServiceTests.BillFixture.Create();
        fixture.Db.TaxFeePolicies.RemoveRange(fixture.Db.TaxFeePolicies);
        await fixture.Db.SaveChangesAsync();
        var service = Service(fixture, new Remote(fixture.Source));
        var state = await service.Request(fixture.Source.Value.TableSessionId, Guid.NewGuid(), "qr", default);
        await Error(409, () => service.Synchronize(state.TableSessionId, state.CheckoutId, default));
        Assert.Empty(await fixture.Db.Bills.ToListAsync());
        Assert.Equal("Frozen", (await service.GetCurrent(state.TableSessionId, "qr", default)).Status);
    }

    [Fact]
    public async Task FailedReleaseKeepsOrdersBlockedUntilAcknowledgementAndRepeatDoesNotCreateAnotherCheckout()
    {
        await using var fixture = await BillServiceTests.BillFixture.Create();
        var remote = new Remote(fixture.Source);
        var service = Service(fixture, remote);
        var state = await service.Request(fixture.Source.Value.TableSessionId, Guid.NewGuid(), "qr", default);
        remote.LoseReleaseReply = true;
        await Error(503, () => service.Cancel(state.TableSessionId, state.CheckoutId, default));
        Assert.Contains(state.CheckoutId, remote.HeldCheckouts);
        var pending = await service.GetCurrent(state.TableSessionId, "qr", default);
        Assert.Equal("ReleasePending", pending.Status);
        Assert.False(pending.OrderingReopened);
        await service.AuthorizeRelease(state.TableSessionId, state.CheckoutId, default);
        remote.LoseReleaseReply = false;
        var released = await Service(fixture, remote).Cancel(state.TableSessionId, state.CheckoutId, default);
        Assert.True(released.OrderingReopened);
        Assert.DoesNotContain(state.CheckoutId, remote.HeldCheckouts);
        Assert.Equal("Released", (await service.Request(state.TableSessionId, state.CheckoutId, "qr", default)).Status);
        Assert.Single(await fixture.Db.BillCheckouts.ToListAsync());
    }

    [Theory]
    [InlineData("admin", true)]
    [InlineData("manager", false)]
    [InlineData("waiter", false)]
    public async Task OnlyCashierCanSynchronizeOrCancel(string role, bool admin)
    {
        await using var fixture = await BillServiceTests.BillFixture.Create();
        var service = Service(fixture, new Remote(fixture.Source));
        var state = await service.Request(fixture.Source.Value.TableSessionId, Guid.NewGuid(), "qr", default);
        fixture.Actor.Scope.RoleId = role;
        fixture.Actor.Scope.IsAdministrator = admin;
        await Error(403, () => service.Cancel(state.TableSessionId, state.CheckoutId, default));
        await Error(403, () => service.Synchronize(state.TableSessionId, state.CheckoutId, default));
    }

    [Fact]
    public async Task DirectReleaseAuthorizationAndPaidBillCannotReopenOrders()
    {
        await using var fixture = await BillServiceTests.BillFixture.Create();
        var service = Service(fixture, new Remote(fixture.Source));
        var state = await service.Request(fixture.Source.Value.TableSessionId, Guid.NewGuid(), "qr", default);
        await Error(409, () => service.AuthorizeRelease(state.TableSessionId, state.CheckoutId, default));
        await service.Synchronize(state.TableSessionId, state.CheckoutId, default);
        var bill = await fixture.Db.Bills.SingleAsync();
        bill.Status = "Paid";
        bill.PaidAt = DateTime.UtcNow;
        await fixture.Db.SaveChangesAsync();
        await Error(409, () => service.Cancel(state.TableSessionId, state.CheckoutId, default));
        Assert.False((await service.GetCurrent(state.TableSessionId, "qr", default)).OrderingReopened);
    }

    private static BillCheckoutService Service(BillServiceTests.BillFixture fixture, Remote remote)
    {
        return new BillCheckoutService(new BillCheckoutRepository(fixture.Db), new BillRepository(fixture.Db),
            new UnitOfWork<BillingDbContext>(fixture.Db), remote, fixture.Source, fixture.Service, fixture.Actor,
            Options.Create(new CashRegisterAccessSettings
            {
                CashierRoleIds = ["cashier"]
            }), TimeProvider.System);
    }

    private static async Task Error(int status, Func<Task> action)
    {
        Assert.Equal(status, (await Assert.ThrowsAsync<BillingException>(action)).StatusCode);
    }

    private sealed class Remote(BillServiceTests.BillSource source) : ICheckoutSourceClient
    {
        public bool LoseBeginReply { get; set; }
        public bool LoseReleaseReply { get; set; }
        public bool LoseBindReply { get; set; }
        public HashSet<Guid> HeldCheckouts { get; } = [];

        public Task VerifyAsync(Guid sessionId, string guestToken, CancellationToken ct)
        {
            return Task.CompletedTask;
        }

        public Task<BillCheckoutDTO> BeginAsync(Guid sessionId, Guid id, string guestToken, CancellationToken ct)
        {
            HeldCheckouts.Add(id);
            if (LoseBeginReply)
            {
                throw new BillingException(503, "Reply lost.");
            }

            return Task.FromResult(new BillCheckoutDTO
            {
                CheckoutId = id,
                TableSessionId = sessionId,
                Status = source.Value.PendingOrderCount > 0 ? "WaitingConfirmation" : "Frozen",
                SourceHash = source.Value.PendingOrderCount > 0 ? null : CheckoutHash.Compute(source.Value.Items.Select(line =>
                    new CheckoutHashLine(line.OrderItemId, line.Item.ItemId, line.Quantity, line.Item.BaseUnitPrice,
                        line.Item.Options.Select(option => new CheckoutHashOption(option.OptionId, option.Quantity, option.ExtraPrice)).ToArray())))
            });
        }

        public Task ReleaseAsync(Guid sessionId, Guid id, CancellationToken ct)
        {
            if (LoseReleaseReply)
            {
                throw new BillingException(503, "Reply lost.");
            }

            HeldCheckouts.Remove(id);
            return Task.CompletedTask;
        }

        public Task BindAsync(Guid sessionId, Guid id, Guid billId, string hash, CancellationToken ct)
        {
            if (LoseBindReply)
            {
                throw new BillingException(503, "Bind reply lost.");
            }

            return Task.CompletedTask;
        }
    }
}
