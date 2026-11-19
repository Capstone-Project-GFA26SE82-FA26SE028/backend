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

public sealed class CancellationRequestTests
{
    [Fact]
    public async Task PartialApprovalUpdatesKitchenBillAndOrderResponseExactlyOnce()
    {
        using var f = new OrderingFlowTests.Fixture();
        var order = await StaffOrder(f);
        var service = Service(f);
        var id = Guid.NewGuid();
        var itemId = order.Items[0].OrderItemId;
        var created = await service.Create(f.Session, id, itemId, 1, "Đặt dư món", "guest", default);
        Assert.True(created.Created);
        Assert.Equal("Pending", created.Result.Status);
        Assert.False((await service.Create(f.Session, id, itemId, 1, "Đặt dư món", "guest", default)).Created);
        f.Staff = true;
        var approved = await service.Approve(f.Session, id, default);
        Assert.Equal("Approved", approved.Status);
        Assert.Equal(f.User, approved.DecidedByRef);
        Assert.NotNull(approved.DecidedAt);
        await service.Approve(f.Session, id, default);
        f.Db.ChangeTracker.Clear();
        Assert.Equal(1, (await f.Db.OrderItems.SingleAsync()).CancelledQuantity);
        Assert.Equal(1, (await f.Db.KitchenTasks.SingleAsync()).Quantity);
        var response = await f.Orders.GetById(f.Session, order.OrderId, "", default);
        Assert.Equal(1, response.Items[0].CancelledQuantity);
        Assert.Equal(1, response.Items[0].RemainingQuantity);
        Assert.Equal(order.TotalAmount / 2, response.TotalAmount);
        var billing = await new BillingOrderService(new OrderRepository(f.Db), new BillingSession())
            .GetForRestaurant(f.Session, f.Restaurant, default);
        Assert.Equal(1, Assert.Single(billing.Items).Quantity);
        var events = await f.Db.Set<Shared.Persistence.Notifications.NotificationOutbox>().ToListAsync();
        Assert.Single(events, value => value.EventKey.Contains("ItemCancellationRequested"));
    }

    [Fact]
    public async Task FullApprovalCancelsTaskAndRemovesBillLine()
    {
        using var f = new OrderingFlowTests.Fixture();
        var order = await StaffOrder(f);
        var service = Service(f);
        var id = Guid.NewGuid();
        await service.Create(f.Session, id, order.Items[0].OrderItemId, 2, "Không dùng nữa", "guest", default);
        f.Staff = true;
        await service.Approve(f.Session, id, default);
        Assert.Equal("Cancelled", (await f.Db.KitchenTasks.SingleAsync()).Status);
        Assert.Equal("Cancelled", (await f.Db.Orders.SingleAsync()).Status);
        Assert.Equal(0, (await f.Orders.GetById(f.Session, order.OrderId, "", default)).TotalAmount);
        Assert.Empty((await new BillingOrderService(new OrderRepository(f.Db), new BillingSession())
            .GetForRestaurant(f.Session, f.Restaurant, default)).Items);
    }

    [Fact]
    public async Task RejectionPreservesFoodAndRecordsReasonAndCannotBeApprovedLater()
    {
        using var f = new OrderingFlowTests.Fixture();
        var order = await StaffOrder(f);
        var service = Service(f);
        var id = Guid.NewGuid();
        await service.Create(f.Session, id, order.Items[0].OrderItemId, 1, "Đặt nhầm", "guest", default);
        f.Staff = true;
        var result = await service.Reject(f.Session, id, "Khách xác nhận giữ món", default);
        Assert.Equal("Rejected", result.Status);
        Assert.Equal("Khách xác nhận giữ món", result.DecisionReason);
        await service.Reject(f.Session, id, "Khách xác nhận giữ món", default);
        Assert.Equal(0, (await f.Db.OrderItems.SingleAsync()).CancelledQuantity);
        Assert.Equal(2, (await f.Db.KitchenTasks.SingleAsync()).Quantity);
        Assert.Equal(409, (await Assert.ThrowsAsync<OrderingException>(() => service.Approve(f.Session, id, default))).StatusCode);
    }

    [Fact]
    public async Task PendingRequestBlocksKitchenSingleBatchAndCheckoutUntilRejected()
    {
        using var f = new OrderingFlowTests.Fixture();
        var order = await StaffOrder(f);
        var service = Service(f);
        var id = Guid.NewGuid();
        await service.Create(f.Session, id, order.Items[0].OrderItemId, 1, "Đặt nhầm", "guest", default);
        f.Staff = true;
        var task = await f.Db.KitchenTasks.SingleAsync();
        var inventory = new KitchenTaskTests.FakeCookingInventoryClient();
        var kitchen = new KitchenTaskService(new KitchenTaskRepository(f.Db), Transaction(f),
            new KitchenAccess(), inventory, TimeProvider.System, new CheckoutLockRepository(f.Db));
        Assert.Equal(409, (await Assert.ThrowsAsync<OrderingException>(() => kitchen.SetStatus(task.KitchenTaskId, "Cooking", null, default))).StatusCode);
        Assert.Equal(409, (await Assert.ThrowsAsync<OrderingException>(() => kitchen.StartBatch([task.KitchenTaskId], default))).StatusCode);
        Assert.Equal(409, (await Assert.ThrowsAsync<OrderingException>(() => kitchen.CompleteBatch([task.KitchenTaskId], default))).StatusCode);
        var checkout = new CheckoutLockService(new CheckoutLockRepository(f.Db), new OrderRepository(f.Db), Transaction(f),
            new CheckoutAccess(), new ReleaseAccess(), TimeProvider.System);
        Assert.Equal(409, (await Assert.ThrowsAsync<OrderingException>(() => checkout.Begin(f.Session, Guid.NewGuid(), "", default))).StatusCode);
        f.Db.ChangeTracker.Clear();
        await service.Reject(f.Session, id, "Giữ món", default);
        await kitchen.SetStatus(task.KitchenTaskId, "Cooking", null, default);
        Assert.Equal("Cooking", (await f.Db.KitchenTasks.SingleAsync()).Status);
    }

    [Fact]
    public async Task UnconfirmedOrderUsesRemainingQuantityWhenConfirmed()
    {
        using var f = new OrderingFlowTests.Fixture();
        var cart = await f.FilledCart();
        var created = await f.Orders.Create(f.Session, "pending", new CreateOrderDTO
        {
            CartId = cart.CartId,
            CartVersion = cart.Version
        }, "guest", default);
        var service = Service(f);
        var id = Guid.NewGuid();
        await service.Create(f.Session, id, created.Order.Items[0].OrderItemId, 1, "Đặt dư", "guest", default);
        f.Staff = true;
        Assert.Equal(409, (await Assert.ThrowsAsync<OrderingException>(() => f.Orders.Confirm(f.Session, created.Order.OrderId, created.Order.Version, default))).StatusCode);
        await service.Approve(f.Session, id, default);
        var current = await f.Orders.GetById(f.Session, created.Order.OrderId, "", default);
        await f.Orders.Confirm(f.Session, created.Order.OrderId, current.Version, default);
        Assert.Equal(1, (await f.Db.KitchenTasks.SingleAsync()).Quantity);
    }

    [Theory]
    [InlineData("Cooking")]
    [InlineData("Ready")]
    [InlineData("Served")]
    public async Task AlreadyPreparedQuantityCannotBeRequested(string status)
    {
        using var f = new OrderingFlowTests.Fixture();
        var order = await StaffOrder(f);
        var task = await f.Db.KitchenTasks.SingleAsync();
        task.Status = status;
        task.OrderItem.Status = status;
        await f.Db.SaveChangesAsync();
        Assert.Equal(409, (await Assert.ThrowsAsync<OrderingException>(() => Service(f).Create(f.Session, Guid.NewGuid(),
            order.Items[0].OrderItemId, 1, "Đặt nhầm", "guest", default))).StatusCode);
    }

    [Fact]
    public async Task DuplicatePendingOversizedAndConflictingIdempotencyAreRejected()
    {
        using var f = new OrderingFlowTests.Fixture();
        var order = await StaffOrder(f);
        var service = Service(f);
        var item = order.Items[0].OrderItemId;
        Assert.Equal(409, (await Assert.ThrowsAsync<OrderingException>(() => service.Create(f.Session, Guid.NewGuid(), item, 3, "Đặt nhầm", "guest", default))).StatusCode);
        var id = Guid.NewGuid();
        await service.Create(f.Session, id, item, 1, "Đặt nhầm", "guest", default);
        Assert.Equal(409, (await Assert.ThrowsAsync<OrderingException>(() => service.Create(f.Session, id, item, 2, "Đặt nhầm", "guest", default))).StatusCode);
        Assert.Equal(409, (await Assert.ThrowsAsync<OrderingException>(() => service.Create(f.Session, Guid.NewGuid(), item, 1, "Đặt nhầm", "guest", default))).StatusCode);
        Assert.Equal(1, await f.Db.CancellationRequests.CountAsync());
    }

    [Fact]
    public async Task SessionBoundaryGuestDecisionAndClosedSessionAreEnforced()
    {
        using var f = new OrderingFlowTests.Fixture();
        var order = await StaffOrder(f);
        var service = Service(f);
        var id = Guid.NewGuid();
        var item = order.Items[0].OrderItemId;
        Assert.Equal(404, (await Assert.ThrowsAsync<OrderingException>(() => service.Create(Guid.NewGuid(), id, item, 1, "Nhầm", "guest", default))).StatusCode);
        await service.Create(f.Session, id, item, 1, "Nhầm", "guest", default);
        Assert.Equal(403, (await Assert.ThrowsAsync<OrderingException>(() => service.Approve(f.Session, id, default))).StatusCode);
        Assert.Equal(404, (await Assert.ThrowsAsync<OrderingException>(() => service.GetById(Guid.NewGuid(), id, "guest", default))).StatusCode);
        f.Staff = true;
        Assert.Equal(403, (await Assert.ThrowsAsync<OrderingException>(() => service.Create(f.Session, Guid.NewGuid(), item, 1, "Nhầm", "", default))).StatusCode);
        f.Closed = true;
        Assert.Equal(409, (await Assert.ThrowsAsync<OrderingException>(() => service.Reject(f.Session, id, "Đóng", default))).StatusCode);
    }

    [Fact]
    public async Task FrozenBillPreventsNewCancellation()
    {
        using var f = new OrderingFlowTests.Fixture();
        var order = await StaffOrder(f);
        f.Db.CheckoutLocks.Add(new CheckoutLock
        {
            CheckoutId = Guid.NewGuid(),
            TableSessionRef = f.Session,
            Status = "Frozen",
            CreatedAt = DateTime.UtcNow
        });
        await f.Db.SaveChangesAsync();
        Assert.Equal(409, (await Assert.ThrowsAsync<OrderingException>(() => Service(f).Create(f.Session, Guid.NewGuid(),
            order.Items[0].OrderItemId, 1, "Nhầm", "guest", default))).StatusCode);
    }

    [Fact]
    public async Task ListFiltersBeforeCountingAndPaging()
    {
        using var f = new OrderingFlowTests.Fixture();
        var order = await StaffOrder(f);
        var service = Service(f);
        var id = Guid.NewGuid();
        await service.Create(f.Session, id, order.Items[0].OrderItemId, 1, "Nhầm", "guest", default);
        f.Staff = true;
        await service.Reject(f.Session, id, "Giữ món", default);
        f.Staff = false;
        await service.Create(f.Session, Guid.NewGuid(), order.Items[0].OrderItemId, 1, "Đổi ý", "guest", default);
        var page = await service.GetAll(f.Session, "Pending", 1, 1, "guest", default);
        Assert.Equal(1, page.TotalCount);
        Assert.Equal("Pending", Assert.Single(page.Items).Status);
        Assert.Empty((await service.GetAll(f.Session, "Pending", 2, 1, "guest", default)).Items);
    }

    [Fact]
    public async Task MixedBatchesOnlyCancelUncookedQuantity()
    {
        using var f = new OrderingFlowTests.Fixture();
        var order = await StaffOrder(f);
        var cooking = await f.Db.KitchenTasks.SingleAsync();
        cooking.Quantity = 1;
        cooking.Status = "Cooking";
        cooking.OrderItem.Status = "Cooking";
        f.Db.KitchenTasks.Add(new KitchenTask
        {
            KitchenTaskId = Guid.NewGuid(),
            KitchenStationId = f.Station,
            OrderItemId = cooking.OrderItemId,
            BatchNumber = 2,
            Quantity = 1,
            Status = "Pending",
            QueuedAt = DateTime.UtcNow,
            Version = new byte[8]
        });
        await f.Db.SaveChangesAsync();
        var service = Service(f);
        Assert.Equal(409, (await Assert.ThrowsAsync<OrderingException>(() => service.Create(f.Session, Guid.NewGuid(),
            cooking.OrderItemId, 2, "Cancel", "guest", default))).StatusCode);
        var id = Guid.NewGuid();
        await service.Create(f.Session, id, cooking.OrderItemId, 1, "Cancel", "guest", default);
        f.Staff = true;
        await service.Approve(f.Session, id, default);
        Assert.Equal("Cooking", cooking.Status);
        Assert.Equal(1, cooking.Quantity);
        Assert.Equal("Cancelled", (await f.Db.KitchenTasks.SingleAsync(task => task.BatchNumber == 2)).Status);
        Assert.Equal(1, cooking.OrderItem.CancelledQuantity);
    }

    [Fact]
    public async Task ApprovalRechecksKitchenStateAndAllowsRejectionAfterConflict()
    {
        using var f = new OrderingFlowTests.Fixture();
        var order = await StaffOrder(f);
        var service = Service(f);
        var id = Guid.NewGuid();
        await service.Create(f.Session, id, order.Items[0].OrderItemId, 1, "Cancel", "guest", default);
        var task = await f.Db.KitchenTasks.SingleAsync();
        task.Status = "Cooking";
        task.OrderItem.Status = "Cooking";
        await f.Db.SaveChangesAsync();
        f.Staff = true;
        Assert.Equal(409, (await Assert.ThrowsAsync<OrderingException>(() => service.Approve(f.Session, id, default))).StatusCode);
        Assert.Equal("Pending", (await f.Db.CancellationRequests.SingleAsync()).Status);
        await service.Reject(f.Session, id, "Already cooking", default);
        Assert.Equal(0, task.OrderItem.CancelledQuantity);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task StaffCancellationUpdatesFoodAndHistoryOnce(int quantity)
    {
        using var f = new OrderingFlowTests.Fixture();
        var order = await StaffOrder(f);
        f.Staff = true;
        var service = Service(f);
        var id = Guid.NewGuid();
        var itemId = order.Items[0].OrderItemId;
        var result = await service.CancelByStaff(f.Session, id, itemId, quantity, "Hết nguyên liệu", default);
        Assert.True(result.Created);
        Assert.Equal("Staff", result.Result.Source);
        Assert.Equal("Approved", result.Result.Status);
        Assert.Equal(f.User, result.Result.RequestedByRef);
        Assert.Equal(f.User, result.Result.DecidedByRef);
        Assert.Equal("Hết nguyên liệu", result.Result.DecisionReason);
        Assert.NotNull(result.Result.DecidedAt);
        Assert.False((await service.CancelByStaff(f.Session, id, itemId, quantity, "Hết nguyên liệu", default)).Created);
        f.Db.ChangeTracker.Clear();
        var line = await f.Db.OrderItems.SingleAsync();
        Assert.Equal(quantity, line.CancelledQuantity);
        var task = await f.Db.KitchenTasks.SingleAsync();
        Assert.Equal(quantity == 2 ? "Cancelled" : "Pending", task.Status);
        if (quantity == 1)
        {
            Assert.Equal(1, task.Quantity);
        }

        var response = await f.Orders.GetById(f.Session, order.OrderId, "", default);
        Assert.Equal(order.TotalAmount * (2 - quantity) / 2, response.TotalAmount);
        f.Staff = false;
        Assert.Equal("Staff", (await service.GetById(f.Session, id, "guest", default)).Source);
        Assert.Equal(409, (await Assert.ThrowsAsync<OrderingException>(() =>
            service.Create(f.Session, id, itemId, quantity, "Hết nguyên liệu", "guest", default))).StatusCode);
    }

    [Fact]
    public async Task StaffCancellationRequiresEmployeeReasonQuantityAndCorrectSession()
    {
        using var f = new OrderingFlowTests.Fixture();
        var order = await StaffOrder(f);
        var service = Service(f);
        var itemId = order.Items[0].OrderItemId;
        Assert.Equal(403, (await Assert.ThrowsAsync<OrderingException>(() =>
            service.CancelByStaff(f.Session, Guid.NewGuid(), itemId, 1, "Reason", default))).StatusCode);
        f.Staff = true;
        Assert.Equal(400, (await Assert.ThrowsAsync<OrderingException>(() =>
            service.CancelByStaff(f.Session, Guid.NewGuid(), itemId, 1, " ", default))).StatusCode);
        Assert.Equal(400, (await Assert.ThrowsAsync<OrderingException>(() =>
            service.CancelByStaff(f.Session, Guid.NewGuid(), itemId, 0, "Reason", default))).StatusCode);
        Assert.Equal(409, (await Assert.ThrowsAsync<OrderingException>(() =>
            service.CancelByStaff(f.Session, Guid.NewGuid(), itemId, 3, "Reason", default))).StatusCode);
        Assert.Equal(404, (await Assert.ThrowsAsync<OrderingException>(() =>
            service.CancelByStaff(Guid.NewGuid(), Guid.NewGuid(), itemId, 1, "Reason", default))).StatusCode);
        f.Closed = true;
        Assert.Equal(409, (await Assert.ThrowsAsync<OrderingException>(() =>
            service.CancelByStaff(f.Session, Guid.NewGuid(), itemId, 1, "Reason", default))).StatusCode);
        Assert.Empty(await f.Db.CancellationRequests.ToListAsync());
    }

    [Fact]
    public async Task StaffCannotBypassGuestRequestOrReuseAnotherOperationKey()
    {
        using var f = new OrderingFlowTests.Fixture();
        var order = await StaffOrder(f);
        var service = Service(f);
        var id = Guid.NewGuid();
        var itemId = order.Items[0].OrderItemId;
        await service.Create(f.Session, id, itemId, 1, "Guest reason", "guest", default);
        f.Staff = true;
        Assert.Equal(409, (await Assert.ThrowsAsync<OrderingException>(() =>
            service.CancelByStaff(f.Session, Guid.NewGuid(), itemId, 1, "Staff reason", default))).StatusCode);
        await service.Reject(f.Session, id, "Keep dish", default);
        Assert.Equal(409, (await Assert.ThrowsAsync<OrderingException>(() =>
            service.CancelByStaff(f.Session, id, itemId, 1, "Guest reason", default))).StatusCode);
        var staffId = Guid.NewGuid();
        await service.CancelByStaff(f.Session, staffId, itemId, 1, "Staff reason", default);
        Assert.Equal(409, (await Assert.ThrowsAsync<OrderingException>(() =>
            service.CancelByStaff(f.Session, staffId, itemId, 2, "Staff reason", default))).StatusCode);
        Assert.Equal(1, (await f.Db.OrderItems.SingleAsync()).CancelledQuantity);
    }

    [Theory]
    [InlineData("Cooking")]
    [InlineData("Ready")]
    [InlineData("Served")]
    public async Task StaffCannotCancelPreparedQuantity(string status)
    {
        using var f = new OrderingFlowTests.Fixture();
        var order = await StaffOrder(f);
        var task = await f.Db.KitchenTasks.SingleAsync();
        task.Status = status;
        task.OrderItem.Status = status;
        await f.Db.SaveChangesAsync();
        f.Staff = true;
        Assert.Equal(409, (await Assert.ThrowsAsync<OrderingException>(() =>
            Service(f).CancelByStaff(f.Session, Guid.NewGuid(), order.Items[0].OrderItemId, 1, "Reason", default))).StatusCode);
    }

    [Fact]
    public async Task StaffCannotCancelFrozenBillOrUseLegacyTaskCancellation()
    {
        using var f = new OrderingFlowTests.Fixture();
        var order = await StaffOrder(f);
        f.Staff = true;
        var task = await f.Db.KitchenTasks.SingleAsync();
        var kitchen = new KitchenTaskService(new KitchenTaskRepository(f.Db), Transaction(f),
            new KitchenAccess(), new KitchenTaskTests.FakeCookingInventoryClient(), TimeProvider.System,
            new CheckoutLockRepository(f.Db));
        Assert.Equal(409, (await Assert.ThrowsAsync<OrderingException>(() =>
            kitchen.SetStatus(task.KitchenTaskId, "Cancelled", null, default))).StatusCode);
        Assert.Equal("Pending", task.Status);
        f.Db.CheckoutLocks.Add(new CheckoutLock
        {
            CheckoutId = Guid.NewGuid(),
            TableSessionRef = f.Session,
            Status = "Frozen",
            CreatedAt = DateTime.UtcNow
        });
        await f.Db.SaveChangesAsync();
        Assert.Equal(409, (await Assert.ThrowsAsync<OrderingException>(() =>
            Service(f).CancelByStaff(f.Session, Guid.NewGuid(), order.Items[0].OrderItemId, 1, "Reason", default))).StatusCode);
        Assert.Equal(0, task.OrderItem.CancelledQuantity);
    }

    private static async Task<OrderDTO> StaffOrder(OrderingFlowTests.Fixture f)
    {
        f.Staff = true;
        var result = await f.Orders.Create(f.Session, Guid.NewGuid().ToString(), new CreateOrderDTO
        {
            Items = f.Lines()
        }, "", default);
        (await f.Db.Orders.SingleAsync(order => order.OrderId == result.Order.OrderId)).RestaurantRef = f.Restaurant;
        await f.Db.SaveChangesAsync();
        f.Staff = false;
        return result.Order;
    }

    private static OrderingTransaction Transaction(OrderingFlowTests.Fixture f)
    {
        return new OrderingTransaction(new UnitOfWork<OrderingDbContext>(f.Db));
    }

    private static CancellationRequestService Service(OrderingFlowTests.Fixture f)
    {
        return new CancellationRequestService(new CancellationRequestRepository(f.Db), new CheckoutLockRepository(f.Db),
            Transaction(f), f, new StaffAccess(), TimeProvider.System);
    }

    private sealed class StaffAccess : ICancellationRequestStaffAccess
    {
        public Task RequireAsync(CancellationToken ct)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class KitchenAccess : IKitchenStationAccess
    {
        public Task RequireAsync(bool write, CancellationToken ct)
        {
            return Task.CompletedTask;
        }

        public Task<Guid> RequireKitchenTaskAccessAsync(CancellationToken ct)
        {
            return Task.FromResult(Guid.NewGuid());
        }
    }

    private sealed class CheckoutAccess : ICheckoutAccess
    {
        public Task RequireAsync(Guid sessionId, string guestToken, bool write, CancellationToken ct)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class ReleaseAccess : ICheckoutReleaseAuthorization
    {
        public Task RequireAsync(Guid sessionId, Guid checkoutId, CancellationToken ct)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class BillingSession : IBillingSessionClient
    {
        public Task<Guid> GetRestaurantIdAsync(Guid sessionId, CancellationToken ct)
        {
            return Task.FromResult(Guid.NewGuid());
        }
    }
}
