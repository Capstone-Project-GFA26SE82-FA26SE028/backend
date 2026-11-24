using Microsoft.EntityFrameworkCore;
using Ordering.BLL.Implements;
using Ordering.BLL.Interfaces;
using Ordering.Common.DTOs;
using Ordering.Common.Exceptions;
using Ordering.DAL.Implements;
using Ordering.DAL.Models;
using Shared.Common.TableTransfers;
using Shared.Persistence.UnitOfWorks;
using Xunit;

namespace Ordering.Tests;

public sealed class TableTransferTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PartialSplitPreservesQuantityPricesOptionsAndKitchenWork(bool confirmed)
    {
        using var f = new OrderingFlowTests.Fixture();
        var item = await Create(f, confirmed);
        var request = Request(f, item, 1);
        var remote = new Sessions(f.Session, f.Restaurant, f.User);
        var service = Service(f, remote);
        await service.Execute(request, default);
        await service.Execute(request, default);
        f.Db.ChangeTracker.Clear();
        var lines = await f.Db.OrderItems.Include(value => value.Order).Include(value => value.OrderItemOptions).ToListAsync();
        Assert.Equal(2, lines.Count);
        Assert.Equal(2, lines.Sum(value => value.Quantity));
        Assert.All(lines, value => Assert.Equal(50000m, value.UnitPriceSnapshot));
        Assert.All(lines, value => Assert.Equal(2, value.OrderItemOptions.Single().Quantity));
        Assert.Single(lines, value => value.Order.TableSessionRef == f.Session);
        Assert.Single(lines, value => value.Order.TableSessionRef == remote.Target);
        Assert.Equal(confirmed ? 2 : 0, await f.Db.KitchenTasks.SumAsync(value => value.Quantity));
        Assert.Equal("Completed", (await f.Db.TableTransfers.SingleAsync()).Status);
    }

    [Fact]
    public async Task MergeMovesExistingOrdersAndKeepsKitchenIds()
    {
        using var f = new OrderingFlowTests.Fixture();
        var item = await Create(f, true);
        var task = await f.Db.KitchenTasks.SingleAsync();
        var taskId = task.KitchenTaskId;
        task.Status = "Cooking";
        item.Status = "Cooking";
        await f.Db.SaveChangesAsync();
        var remote = new Sessions(f.Session, f.Restaurant, f.User);
        var request = Request(f, item, 2);
        request.Mode = "Merge";
        request.Items.Clear();
        await Service(f, remote).Execute(request, default);
        Assert.Equal(remote.Target, (await f.Db.Orders.SingleAsync()).TableSessionRef);
        Assert.Equal(taskId, (await f.Db.KitchenTasks.SingleAsync()).KitchenTaskId);
        Assert.Equal("Cooking", (await f.Db.KitchenTasks.SingleAsync()).Status);
    }

    [Theory]
    [InlineData("Cooking")]
    [InlineData("Ready")]
    [InlineData("Served")]
    public async Task PartialPreparedItemIsRejectedButWholeLineCanMove(string status)
    {
        using var f = new OrderingFlowTests.Fixture();
        var item = await Create(f, true);
        item.Status = status;
        (await f.Db.KitchenTasks.SingleAsync()).Status = status;
        await f.Db.SaveChangesAsync();
        var remote = new Sessions(f.Session, f.Restaurant, f.User);
        Assert.Equal(409, (await Assert.ThrowsAsync<OrderingException>(() =>
            Service(f, remote).Execute(Request(f, item, 1), default))).StatusCode);
        Assert.Equal("Aborted", remote.State);
        Assert.Equal(2, (await f.Db.OrderItems.SingleAsync()).Quantity);
        remote = new Sessions(f.Session, f.Restaurant, f.User);
        await Service(f, remote).Execute(Request(f, item, 2), default);
        Assert.Equal(remote.Target, (await f.Db.OrderItems.Include(value => value.Order).SingleAsync()).Order.TableSessionRef);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CheckoutOrExistingBillBlocksTransfer(bool checkout)
    {
        using var f = new OrderingFlowTests.Fixture();
        var item = await Create(f, true);
        if (checkout)
        {
            f.Db.CheckoutLocks.Add(new CheckoutLock
            {
                CheckoutId = Guid.NewGuid(),
                TableSessionRef = f.Session,
                Status = "Frozen",
                CreatedAt = DateTime.UtcNow
            });
            await f.Db.SaveChangesAsync();
        }
        var remote = new Sessions(f.Session, f.Restaurant, f.User) { Allowed = checkout };
        await Assert.ThrowsAsync<OrderingException>(() => Service(f, remote).Execute(Request(f, item, 1), default));
        Assert.Equal(2, (await f.Db.OrderItems.SingleAsync()).Quantity);
        Assert.Equal("Aborted", remote.State);
    }

    [Fact]
    public async Task LostFinishReplyCanResumeWithoutMovingAgain()
    {
        using var f = new OrderingFlowTests.Fixture();
        var item = await Create(f, true);
        var request = Request(f, item, 1);
        var remote = new Sessions(f.Session, f.Restaurant, f.User) { LoseReply = true };
        await Assert.ThrowsAsync<InvalidOperationException>(() => Service(f, remote).Execute(request, default));
        f.Db.ChangeTracker.Clear();
        Assert.Equal("Applied", (await f.Db.TableTransfers.SingleAsync()).Status);
        var transaction = new OrderingTransaction(new UnitOfWork<OrderingDbContext>(f.Db));
        await Assert.ThrowsAsync<OrderingException>(() => transaction.ExecuteAsync(async () =>
        {
            await new CheckoutLockRepository(f.Db).LockSessionAsync(f.Session, default);
            return true;
        }, default));
        await Service(f, remote).Execute(request, default);
        Assert.Equal(2, await f.Db.OrderItems.SumAsync(value => value.Quantity));
        Assert.Equal(2, await f.Db.OrderItems.CountAsync());
        Assert.Equal("Completed", (await f.Db.TableTransfers.SingleAsync()).Status);
    }

    [Fact]
    public async Task PendingCancellationAndExcessQuantityAreRejected()
    {
        using var f = new OrderingFlowTests.Fixture();
        var item = await Create(f, true);
        var remote = new Sessions(f.Session, f.Restaurant, f.User);
        await Assert.ThrowsAsync<OrderingException>(() => Service(f, remote).Execute(Request(f, item, 3), default));
        Assert.Equal(2, (await f.Db.OrderItems.SingleAsync()).Quantity);
        f.Db.CancellationRequests.Add(new CancellationRequest
        {
            CancellationRequestId = Guid.NewGuid(),
            OrderItemId = item.OrderItemId,
            Quantity = 1,
            Reason = "Khách yêu cầu hủy",
            RequestedByRef = f.Guest,
            RequestedAt = DateTime.UtcNow,
            Status = "Pending"
        });
        await f.Db.SaveChangesAsync();
        remote = new Sessions(f.Session, f.Restaurant, f.User);
        await Assert.ThrowsAsync<OrderingException>(() => Service(f, remote).Execute(Request(f, item, 1), default));
        Assert.Equal(2, (await f.Db.OrderItems.SingleAsync()).Quantity);
    }

    private static async Task<OrderItem> Create(OrderingFlowTests.Fixture f, bool confirmed)
    {
        if (confirmed)
        {
            f.Staff = true;
            await f.Orders.Create(f.Session, Guid.NewGuid().ToString(), new CreateOrderDTO
            {
                TableId = f.Table,
                Items = f.Lines()
            }, "", default);
        }
        else
        {
            var cart = await f.FilledCart();
            await f.Orders.Create(f.Session, Guid.NewGuid().ToString(), new CreateOrderDTO
            {
                CartId = cart.CartId,
                CartVersion = cart.Version
            }, "guest", default);
        }
        return await f.Db.OrderItems.SingleAsync();
    }

    private static TableTransferDTO Request(OrderingFlowTests.Fixture f, OrderItem item, int quantity)
    {
        return new TableTransferDTO
        {
            OperationId = Guid.NewGuid(),
            SourceSessionId = f.Session,
            TargetTableId = Guid.NewGuid(),
            Mode = "Split",
            Reason = "Khách tách bàn",
            Items = [new TableTransferLineDTO
            {
                OrderItemId = item.OrderItemId,
                Quantity = quantity
            }]
        };
    }

    private static TableTransferService Service(OrderingFlowTests.Fixture f, Sessions remote)
    {
        return new TableTransferService(new TableTransferRepository(f.Db), new OrderRepository(f.Db),
            new CheckoutLockRepository(f.Db), new OrderingTransaction(new UnitOfWork<OrderingDbContext>(f.Db)),
            remote, TimeProvider.System);
    }

    internal sealed class Sessions(Guid source, Guid restaurant, Guid actor) : ITableTransferSessionClient
    {
        public Guid Target { get; } = Guid.NewGuid();
        public string State { get; private set; } = "Prepared";
        public bool Allowed { get; set; } = true;
        public bool LoseReply { get; set; }
        private TableTransferDTO input = null!;

        public Task<bool> CanTransferAsync(Guid sourceId, Guid targetId, CancellationToken ct)
        {
            return Task.FromResult(Allowed);
        }

        public Task<TableTransferResultDTO> PrepareAsync(TableTransferDTO request, CancellationToken ct)
        {
            input = request;
            return GetAsync(request.OperationId, ct);
        }

        public Task<TableTransferResultDTO> FinishAsync(Guid id, bool abort, CancellationToken ct)
        {
            State = abort ? "Aborted" : "Completed";
            if (LoseReply && !abort)
            {
                LoseReply = false;
                throw new InvalidOperationException("Simulated lost reply after commit");
            }
            return GetAsync(id, ct);
        }

        public Task<TableTransferResultDTO> GetAsync(Guid id, CancellationToken ct)
        {
            return Task.FromResult(new TableTransferResultDTO
            {
                OperationId = id,
                SourceSessionId = source,
                TargetSessionId = Target,
                TargetTableId = input.TargetTableId,
                RestaurantId = restaurant,
                ActorId = actor,
                Status = State
            });
        }
    }
}
