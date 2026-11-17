using Microsoft.EntityFrameworkCore;
using Ordering.BLL.Implements;
using Ordering.BLL.Interfaces;
using Ordering.Common.DTOs;
using Ordering.Common.Exceptions;
using Ordering.DAL.Implements;
using Ordering.DAL.Models;
using Shared.Persistence.UnitOfWorks;
using Xunit;

namespace Ordering.Tests;

public sealed class TakeawayOrderTests
{
    [Fact]
    public async Task NotificationOutboxFollowsCommittedRefundTransitionsAndRetryIsIdempotent()
    {
        using var app = new OrderingFlowTests.Fixture();
        app.PaymentState = "Paid";
        var order = (await app.Orders.CreateTakeaway(app.Restaurant, "outbox", await Cart(app), default)).Order;
        var rows = app.Db.Set<Shared.Persistence.Notifications.NotificationOutbox>();
        Assert.Equal(2, await rows.CountAsync());
        await app.Orders.GetTakeawayById(app.Restaurant, order.OrderId, default);
        Assert.Equal(2, await rows.CountAsync());
        await app.Orders.CancelTakeaway(app.Restaurant, order.OrderId, order.Version, default);
        await app.Orders.SaveRefundAccount(app.Restaurant, order.OrderId, RefundAccount(), default);
        await app.Orders.SaveRefundAccount(app.Restaurant, order.OrderId, RefundAccount(), default);
        app.Staff = true;
        app.StaffRole = "cashier";
        await app.Orders.VerifyTakeawayRefund(app.Restaurant, order.OrderId, "bank", "https://evidence.test/file", default);
        var values = await rows.AsNoTracking().ToListAsync();
        Assert.Equal(6, values.Count);
        Assert.Equal(6, values.Select(value => value.EventKey).Distinct().Count());
        Assert.DoesNotContain("00123456789", string.Join(" ", values.Select(value => value.Payload)));
        var delivery = new Shared.Persistence.Notifications.NotificationOutboxRepository<OrderingDbContext>(app.Db);
        var first = values[0];
        await delivery.RetryAsync(first.NotificationOutboxId, 0, "Unavailable", default);
        Assert.DoesNotContain(await delivery.GetPendingAsync(default), value => value.NotificationOutboxId == first.NotificationOutboxId);
        await delivery.CompleteAsync(first.NotificationOutboxId, default);
        await delivery.RetryAsync(first.NotificationOutboxId, 1, "LateFailure", default);
        Assert.NotNull((await rows.AsNoTracking().SingleAsync(value => value.NotificationOutboxId == first.NotificationOutboxId)).ProcessedAt);
    }

    [Fact]
    public async Task NotificationOutboxRollsBackWithOrderChange()
    {
        using var app = new OrderingFlowTests.Fixture();
        var result = (await app.Orders.CreateTakeaway(app.Restaurant, "rollback-outbox", await Cart(app), default)).Order;
        await using (var transaction = await app.Db.Database.BeginTransactionAsync())
        {
            var order = await app.Db.Orders.SingleAsync(value => value.OrderId == result.OrderId);
            order.Status = "Cancelled";
            await app.Db.SaveChangesAsync();
            Assert.Single(await app.Db.Set<Shared.Persistence.Notifications.NotificationOutbox>().ToListAsync());
            await transaction.RollbackAsync();
        }
        app.Db.ChangeTracker.Clear();
        Assert.Empty(await app.Db.Set<Shared.Persistence.Notifications.NotificationOutbox>().ToListAsync());
        Assert.Equal("PendingPayment", (await app.Db.Orders.SingleAsync()).Status);
    }

    private static RefundAccountDTO RefundAccount()
    {
        return new RefundAccountDTO
        {
            BankName = "Example Bank",
            AccountNumber = "00123456789",
            AccountHolder = "NGUYEN VAN AN"
        };
    }

    [Fact]
    public async Task RefundAccountRequiresPendingRefundAndIsPrivateAndImmutable()
    {
        using var app = new OrderingFlowTests.Fixture();
        app.PaymentState = "Paid";
        var order = (await app.Orders.CreateTakeaway(app.Restaurant, "account", await Cart(app), default)).Order;
        await Error(409, () => app.Orders.SaveRefundAccount(app.Restaurant, order.OrderId, RefundAccount(), default));
        await app.Orders.CancelTakeaway(app.Restaurant, order.OrderId, order.Version, default);
        var invalid = RefundAccount();
        invalid.AccountNumber = "not-a-number";
        await Error(400, () => app.Orders.SaveRefundAccount(app.Restaurant, order.OrderId, invalid, default));
        var saved = await app.Orders.SaveRefundAccount(app.Restaurant, order.OrderId, RefundAccount(), default);
        Assert.True(saved.Submitted);
        Assert.Equal("00123456789", saved.AccountNumber);
        Assert.True((await app.Orders.SaveRefundAccount(app.Restaurant, order.OrderId, RefundAccount(), default)).Submitted);
        var changed = RefundAccount();
        changed.AccountNumber = "9999999";
        await Error(409, () => app.Orders.SaveRefundAccount(app.Restaurant, order.OrderId, changed, default));
        app.CustomerUser = Guid.NewGuid();
        await Error(404, () => app.Orders.GetRefundAccount(app.Restaurant, order.OrderId, default));
        await Error(404, () => app.Orders.SaveRefundAccount(app.Restaurant, order.OrderId, RefundAccount(), default));
        app.Staff = true;
        await Error(403, () => app.Orders.GetRefundAccount(app.Restaurant, order.OrderId, default));
        app.StaffRole = "cashier";
        Assert.Equal(saved.AccountNumber, (await app.Orders.GetRefundAccount(app.Restaurant, order.OrderId, default)).AccountNumber);
        await Error(403, () => app.Orders.GetRefundAccount(Guid.NewGuid(), order.OrderId, default));
        await Error(403, () => app.Orders.SaveRefundAccount(app.Restaurant, order.OrderId, RefundAccount(), default));
    }

    private static async Task<SubmitTakeawayDTO> Cart(OrderingFlowTests.Fixture app)
    {
        var cart = await app.Carts.Takeaway(app.Restaurant, null, "Create", null, default);
        cart = await app.Carts.Takeaway(app.Restaurant, cart.CartId, "Update", new CartWriteDTO
        {
            Version = cart.Version,
            Items = app.Lines()
        }, default);
        return new SubmitTakeawayDTO
        {
            CartId = cart.CartId,
            CartVersion = cart.Version,
            PickupName = "Online customer",
            PickupPhone = "0901234567",
        };
    }

    [Fact]
    public async Task CustomerOrdersWithoutQrOrTableAndOnlyVerifiedPaymentAwaitsAcceptance()
    {
        using var app = new OrderingFlowTests.Fixture();
        app.Closed = true;
        var request = await Cart(app);
        var (order, created) = await app.Orders.CreateTakeaway(app.Restaurant, "online", request, default);
        Assert.True(created);
        Assert.Null(order.ReadyForPickupAt);
        Assert.Null((await app.Db.Orders.SingleAsync()).PickupAt);
        Assert.Null(order.TableSessionId);
        Assert.Equal("Customer", order.Source);
        Assert.Equal("PendingPayment", order.Status);
        Assert.Equal("Pending", order.PaymentStatus);
        Assert.Equal("Takeaway", app.QuotedChannel);
        Assert.Equal(120000m, order.TotalAmount);
        Assert.Empty(await app.Db.KitchenTasks.ToListAsync());
        var retry = await app.Orders.CreateTakeaway(app.Restaurant, "online", request, default);
        Assert.False(retry.Created);
        Assert.Equal(order.OrderId, retry.Order.OrderId);
        app.Staff = true;
        await Error(409, () => app.Orders.AcceptTakeaway(app.Restaurant, order.OrderId, retry.Order.Version, default));
        Assert.Empty(await app.Db.KitchenTasks.ToListAsync());
        app.PaymentState = "Paid";
        var paid = await app.Orders.GetTakeawayById(app.Restaurant, order.OrderId, default);
        Assert.Equal("PendingConfirmation", paid.Status);
        Assert.Null(paid.PickupCode);
        Assert.Empty(await app.Db.KitchenTasks.ToListAsync());
        var accepted = await app.Orders.AcceptTakeaway(app.Restaurant, order.OrderId, paid.Version, default);
        Assert.Equal("Accepted", accepted.Status);
        await app.Orders.AcceptTakeaway(app.Restaurant, order.OrderId, paid.Version, default);
        Assert.Single(await app.Db.KitchenTasks.ToListAsync());
    }

    [Fact]
    public async Task SoldOutAtCheckoutBlocksPaymentAndPreservesCart()
    {
        using var app = new OrderingFlowTests.Fixture();
        var request = await Cart(app);
        app.Valid = false;
        await Error(409, () => app.Orders.CreateTakeaway(app.Restaurant, "soldout", request, default));
        Assert.Equal(0, app.PaymentCalls);
        Assert.Empty(await app.Db.Orders.ToListAsync());
        Assert.Single(await app.Db.CartItems.ToListAsync());
    }

    [Fact]
    public async Task OwnershipStaffSubmissionAndChangedKeyBodyAreRejected()
    {
        using var app = new OrderingFlowTests.Fixture();
        var request = await Cart(app);
        var order = (await app.Orders.CreateTakeaway(app.Restaurant, "owner", request, default)).Order;
        request.PickupPhone = "0909999999";
        await Error(409, () => app.Orders.CreateTakeaway(app.Restaurant, "owner", request, default));
        app.CustomerUser = Guid.NewGuid();
        await Error(404, () => app.Orders.GetTakeawayById(app.Restaurant, order.OrderId, default));
        Assert.Empty(await app.Orders.GetAllTakeaway(app.Restaurant, null, 1, 20, default));
        await Error(404, () => app.Carts.Takeaway(app.Restaurant, request.CartId, "Get", null, default));
        app.Staff = true;
        await Error(403, () => app.Orders.CreateTakeaway(app.Restaurant, "staff", request, default));
        await Error(403, () => app.Orders.GetTakeawayById(Guid.NewGuid(), order.OrderId, default));
    }

    [Fact]
    public async Task PaidCustomerCancellationBeforeAcceptanceTracksVerifiedRefundSeparately()
    {
        using var app = new OrderingFlowTests.Fixture();
        app.PaymentState = "Paid";
        var order = (await app.Orders.CreateTakeaway(app.Restaurant, "cancel", await Cart(app), default)).Order;
        var cancelled = await app.Orders.CancelTakeaway(app.Restaurant, order.OrderId, order.Version, default);
        Assert.Equal("Cancelled", cancelled.Status);
        Assert.Equal("RefundPending", cancelled.PaymentStatus);
        Assert.Null(cancelled.RefundedAt);
        Assert.Empty(await app.Db.KitchenTasks.ToListAsync());
        app.Staff = true;
        await Error(409, () => app.Orders.AcceptTakeaway(app.Restaurant, order.OrderId, cancelled.Version, default));
        await Error(403, () => app.Orders.VerifyTakeawayRefund(app.Restaurant, order.OrderId, "bank", "https://evidence.example/1", default));
        app.StaffRole = "cashier";
        await Error(409, () => app.Orders.VerifyTakeawayRefund(app.Restaurant, order.OrderId, "bank", "https://evidence.example/1", default));
        app.Staff = false;
        await app.Orders.SaveRefundAccount(app.Restaurant, order.OrderId, RefundAccount(), default);
        app.Staff = true;
        var refunded = await app.Orders.VerifyTakeawayRefund(app.Restaurant, order.OrderId, "bank", "https://evidence.example/1", default);
        Assert.Equal("Cancelled", refunded.Status);
        Assert.Equal("Refunded", refunded.PaymentStatus);
        Assert.NotNull(refunded.RefundedAt);
    }

    [Fact]
    public async Task RejectionRequiresReasonAndRefundDoesNotChangeRejectedState()
    {
        using var app = new OrderingFlowTests.Fixture();
        app.PaymentState = "Paid";
        var order = (await app.Orders.CreateTakeaway(app.Restaurant, "reject", await Cart(app), default)).Order;
        app.Staff = true;
        await Error(400, () => app.Orders.RejectTakeaway(app.Restaurant, order.OrderId, order.Version, "", default));
        var rejected = await app.Orders.RejectTakeaway(app.Restaurant, order.OrderId, order.Version, "Cannot fulfil", default);
        Assert.Equal("Rejected", rejected.Status);
        Assert.Equal("RefundPending", rejected.PaymentStatus);
        Assert.Equal("Cannot fulfil", rejected.CancellationReason);
        Assert.Empty(await app.Db.KitchenTasks.ToListAsync());
    }

    [Fact]
    public async Task UncertainCancellationBlocksAcceptanceAndReconcilesOnRetry()
    {
        using var app = new OrderingFlowTests.Fixture();
        var order = (await app.Orders.CreateTakeaway(app.Restaurant, "uncertain", await Cart(app), default)).Order;
        app.PaymentUnavailable = true;
        await Error(503, () => app.Orders.CancelTakeaway(app.Restaurant, order.OrderId, order.Version, default));
        Assert.True((await app.Db.Orders.SingleAsync()).CancellationRequested);
        Assert.Empty(await app.Db.KitchenTasks.ToListAsync());
        app.PaymentUnavailable = false;
        app.PaymentState = "Paid";
        var cancelled = await app.Orders.CancelTakeaway(app.Restaurant, order.OrderId, order.Version, default);
        Assert.Equal("Cancelled", cancelled.Status);
        Assert.Equal("RefundPending", cancelled.PaymentStatus);
    }

    [Fact]
    public async Task AcceptedOrderCannotBeCancelledAndPickupRequiresCodeAndPhysicalHandover()
    {
        using var app = new OrderingFlowTests.Fixture();
        app.PaymentState = "Paid";
        var order = (await app.Orders.CreateTakeaway(app.Restaurant, "pickup", await Cart(app), default)).Order;
        var code = order.PickupCode!;
        app.Staff = true;
        var accepted = await app.Orders.AcceptTakeaway(app.Restaurant, order.OrderId, order.Version, default);
        app.Staff = false;
        await Error(409, () => app.Orders.CancelTakeaway(app.Restaurant, order.OrderId, accepted.Version, default));
        var dependencies = new KitchenDependencies(app.User, app.Restaurant);
        var kitchen = new KitchenTaskService(new KitchenTaskRepository(app.Db),
            new OrderingTransaction(new UnitOfWork<OrderingDbContext>(app.Db)), dependencies, dependencies,
            TimeProvider.System, new CheckoutLockRepository(app.Db));
        var task = await app.Db.KitchenTasks.SingleAsync();
        await Error(409, () => kitchen.SetStatus(task.KitchenTaskId, "Ready", null, default));
        await Error(409, () => kitchen.SetStatus(task.KitchenTaskId, "Cancelled", null, default));
        await kitchen.StartBatch([task.KitchenTaskId], default);
        Assert.Equal("Cooking", (await app.Db.Orders.SingleAsync()).Status);
        Assert.Null((await app.Db.Orders.SingleAsync()).ReadyForPickupAt);
        await kitchen.CompleteBatch([task.KitchenTaskId], default);
        Assert.Equal(1, dependencies.Deductions);
        var ready = await app.Orders.GetTakeawayById(app.Restaurant, order.OrderId, default);
        Assert.Equal("ReadyForPickup", ready.Status);
        Assert.NotNull(ready.ReadyForPickupAt);
        app.Staff = true;
        await Error(400, () => app.Orders.CollectTakeaway(app.Restaurant, order.OrderId, ready.Version, "bad", true, default));
        await Error(400, () => app.Orders.CollectTakeaway(app.Restaurant, order.OrderId, ready.Version, code, false, default));
        var completed = await app.Orders.CollectTakeaway(app.Restaurant, order.OrderId, ready.Version, code, true, default);
        Assert.Equal("Completed", completed.Status);
        await app.Orders.CollectTakeaway(app.Restaurant, order.OrderId, ready.Version, code, true, default);
        Assert.Single(task.Events, value => value.ToStatus == "Served");
        await Error(409, () => kitchen.RemakeTask(task.KitchenTaskId, "again", default));
        var otherKitchen = new KitchenTaskService(new KitchenTaskRepository(app.Db),
            new OrderingTransaction(new UnitOfWork<OrderingDbContext>(app.Db)), new KitchenDependencies(app.User, Guid.NewGuid()),
            dependencies, TimeProvider.System, new CheckoutLockRepository(app.Db));
        await Error(404, () => otherKitchen.GetById(task.KitchenTaskId, default));
    }

    private static async Task Error(int status, Func<Task> action)
    {
        Assert.Equal(status, (await Assert.ThrowsAsync<OrderingException>(action)).StatusCode);
    }

    private sealed class KitchenDependencies(Guid user, Guid restaurant) : IKitchenStationAccess, ICookingInventoryClient
    {
        public int Deductions { get; private set; }

        public Task<IReadOnlyList<Guid>> GetOnlineRestaurantIdsAsync(CancellationToken ct)
        {
            return Task.FromResult<IReadOnlyList<Guid>>([restaurant]);
        }

        public Task RequireAsync(bool write, CancellationToken ct)
        {
            return Task.CompletedTask;
        }

        public Task<Guid> RequireKitchenTaskAccessAsync(CancellationToken ct)
        {
            return Task.FromResult(user);
        }

        public Task DeductCookingIngredientsAsync(Guid id, IReadOnlyList<KitchenTask> tasks, CancellationToken ct)
        {
            Deductions++;
            return Task.CompletedTask;
        }
    }
}
