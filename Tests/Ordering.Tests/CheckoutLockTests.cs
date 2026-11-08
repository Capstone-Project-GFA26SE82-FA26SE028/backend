using Microsoft.EntityFrameworkCore;
using Ordering.BLL.Implements;
using Ordering.BLL.Interfaces;
using Ordering.Common.DTOs;
using Ordering.Common.Exceptions;
using Ordering.DAL.Implements;
using Shared.Persistence.UnitOfWorks;
using Xunit;

namespace Ordering.Tests;

public sealed class CheckoutLockTests
{
    [Fact]
    public async Task PendingOrdersRemainPendingAndAreIncludedAfterWaiterConfirmation()
    {
        using var fixture = new OrderingFlowTests.Fixture();
        var cart = await fixture.FilledCart();
        var order = await fixture.Orders.Create(fixture.Session, "before-checkout", new CreateOrderDTO
        {
            CartId = cart.CartId,
            CartVersion = cart.Version
        }, "guest", default);
        var service = Service(fixture);
        var id = Guid.NewGuid();
        var waiting = await service.Begin(fixture.Session, id, "guest", default);
        Assert.Equal("WaitingConfirmation", waiting.Status);
        Assert.Null(waiting.SourceHash);
        Assert.Equal("PendingConfirmation", (await fixture.Db.Orders.SingleAsync()).Status);
        fixture.Staff = true;
        await fixture.Orders.Confirm(fixture.Session, order.Order.OrderId, order.Order.Version, default);
        var frozen = await service.Begin(fixture.Session, id, "guest", default);
        Assert.Equal("Frozen", frozen.Status);
        Assert.Equal(64, frozen.SourceHash!.Length);
        Assert.Single(await fixture.Db.CheckoutLocks.ToListAsync());
        fixture.Db.OrderItems.Single().Status = "Served";
        await fixture.Db.SaveChangesAsync();
        Assert.Equal(frozen.SourceHash, (await service.Begin(fixture.Session, id, "guest", default)).SourceHash);
    }

    [Fact]
    public async Task NewOrdersFromAnotherDeviceAreBlockedButSuccessfulOrderReplayIsPreserved()
    {
        using var fixture = new OrderingFlowTests.Fixture();
        fixture.Staff = true;
        var body = new CreateOrderDTO
        {
            Items = fixture.Lines()
        };
        var before = await fixture.Orders.Create(fixture.Session, "before-checkout", body, string.Empty, default);
        await Service(fixture).Begin(fixture.Session, Guid.NewGuid(), "guest", default);
        var replay = await fixture.Orders.Create(fixture.Session, "before-checkout", body, string.Empty, default);
        Assert.False(replay.Created);
        Assert.Equal(before.Order.OrderId, replay.Order.OrderId);
        var error = await Assert.ThrowsAsync<OrderingException>(() => fixture.Orders.Create(fixture.Session,
            "another-device", body, string.Empty, default));
        Assert.Equal(409, error.StatusCode);
        Assert.Single(await fixture.Db.Orders.ToListAsync());
    }

    [Fact]
    public async Task ReleaseTombstonePreventsLateBeginAndOldReleaseCannotUnlockNewCheckout()
    {
        using var fixture = new OrderingFlowTests.Fixture();
        var service = Service(fixture);
        var old = Guid.NewGuid();
        await service.Release(fixture.Session, old, default);
        Assert.Equal("Released", (await service.Begin(fixture.Session, old, "guest", default)).Status);
        var next = Guid.NewGuid();
        await service.Begin(fixture.Session, next, "guest", default);
        await service.Release(fixture.Session, old, default);
        Assert.Equal(next, (await fixture.Db.CheckoutLocks.SingleAsync(value => value.ReleasedAt == null)).CheckoutId);
    }

    [Fact]
    public async Task BindRequiresMatchingSessionHashAndUnreleasedCheckout()
    {
        using var fixture = new OrderingFlowTests.Fixture();
        var service = Service(fixture);
        var id = Guid.NewGuid();
        var state = await service.Begin(fixture.Session, id, "guest", default);
        await Assert.ThrowsAsync<OrderingException>(() => service.Bind(fixture.Session, id, Guid.NewGuid(), "invalid", default));
        var bill = Guid.NewGuid();
        await service.Bind(fixture.Session, id, bill, state.SourceHash!, default);
        Assert.Equal(bill, (await fixture.Db.CheckoutLocks.SingleAsync()).BillIdRef);
        await service.Release(fixture.Session, id, default);
        await Assert.ThrowsAsync<OrderingException>(() => service.Bind(fixture.Session, id, bill, state.SourceHash!, default));
    }

    private static CheckoutLockService Service(OrderingFlowTests.Fixture fixture)
    {
        return new CheckoutLockService(new CheckoutLockRepository(fixture.Db), new OrderRepository(fixture.Db),
            new OrderingTransaction(new UnitOfWork<Ordering.DAL.Models.OrderingDbContext>(fixture.Db)),
            new Access(), new Authorization(), TimeProvider.System);
    }

    private sealed class Access : ICheckoutAccess
    {
        public Task RequireAsync(Guid sessionId, string guestToken, bool write, CancellationToken ct)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class Authorization : ICheckoutReleaseAuthorization
    {
        public Task RequireAsync(Guid sessionId, Guid checkoutId, CancellationToken ct)
        {
            return Task.CompletedTask;
        }
    }
}
