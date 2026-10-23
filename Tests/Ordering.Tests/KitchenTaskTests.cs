using Microsoft.Data.Sqlite;
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

public sealed class KitchenTaskTests
{
    [Fact]
    public async Task StatusTransitionsFollowCookingFlowAndLogEvents()
    {
        using var fixture = new Fixture();
        var (task, _) = await fixture.SeedTaskAsync("Pending", 0);

        // 1. Pending -> Cooking
        fixture.Clock.Advance(TimeSpan.FromMinutes(5));
        var cookingTask = await fixture.Service.SetStatus(task.KitchenTaskId, "Cooking", null, default);
        Assert.Equal("Cooking", cookingTask.Status);
        Assert.NotNull(cookingTask.CookingAt);
        Assert.Equal("Cooking", (await fixture.Db.OrderItems.FirstAsync(i => i.OrderItemId == task.OrderItemId)).Status);

        var events = await fixture.Db.Events.Where(e => e.KitchenTaskId == task.KitchenTaskId).ToListAsync();
        Assert.Single(events);
        Assert.Equal("Pending", events[0].FromStatus);
        Assert.Equal("Cooking", events[0].ToStatus);

        // 2. Cooking -> Ready
        fixture.Clock.Advance(TimeSpan.FromMinutes(10));
        var readyTask = await fixture.Service.SetStatus(task.KitchenTaskId, "Ready", null, default);
        Assert.Equal("Ready", readyTask.Status);
        Assert.NotNull(readyTask.ReadyAt);
        Assert.Equal("Ready", (await fixture.Db.OrderItems.FirstAsync(i => i.OrderItemId == task.OrderItemId)).Status);

        events = await fixture.Db.Events.Where(e => e.KitchenTaskId == task.KitchenTaskId).ToListAsync();
        Assert.Equal(2, events.Count);
        Assert.Equal("Cooking", events[1].FromStatus);
        Assert.Equal("Ready", events[1].ToStatus);

        // When all order items are Ready, Order status becomes Ready
        var order = await fixture.Db.Orders.FirstAsync(o => o.OrderId == task.OrderItem.OrderId);
        Assert.Equal("Ready", order.Status);
    }

    [Fact]
    public async Task InvalidStatusTransitionsAreRejected()
    {
        using var fixture = new Fixture();
        var (task, _) = await fixture.SeedTaskAsync("Pending", 0);

        await fixture.Service.SetStatus(task.KitchenTaskId, "Cooking", null, default);

        var err = await Assert.ThrowsAsync<OrderingException>(() =>
            fixture.Service.SetStatus(task.KitchenTaskId, "Cooking", null, default));
        Assert.Equal(400, err.StatusCode);

        var err2 = await Assert.ThrowsAsync<OrderingException>(() =>
            fixture.Service.SetStatus(task.KitchenTaskId, "UnknownStatus", null, default));
        Assert.Equal(400, err2.StatusCode);
    }

    [Fact]
    public async Task PriorityCanBeUpdatedForVipTickets()
    {
        using var fixture = new Fixture();
        var (task, _) = await fixture.SeedTaskAsync("Pending", 0);

        var updated = await fixture.Service.SetPriority(task.KitchenTaskId, 5, null, default);
        Assert.Equal(5, updated.Priority);

        var err = await Assert.ThrowsAsync<OrderingException>(() =>
            fixture.Service.SetPriority(task.KitchenTaskId, -1, null, default));
        Assert.Equal(400, err.StatusCode);
    }

    [Fact]
    public async Task GetAllFiltersByStationAndStatusWithCorrectOrdering()
    {
        using var fixture = new Fixture();
        var (task1, station1) = await fixture.SeedTaskAsync("Pending", 10);
        fixture.Clock.Advance(TimeSpan.FromMinutes(1));
        var (task2, _) = await fixture.SeedTaskAsync("Pending", 20, station1);
        var (task3, _) = await fixture.SeedTaskAsync("Ready", 0, station1);

        // Active only (default: Pending + Cooking)
        var activePage = await fixture.Service.GetAll(1, 10, station1, "active", null, null, default);
        Assert.Equal(2, activePage.TotalCount);
        // Priority 20 should come before priority 10
        Assert.Equal(task2.KitchenTaskId, activePage.Items[0].KitchenTaskId);
        Assert.Equal(task1.KitchenTaskId, activePage.Items[1].KitchenTaskId);

        // Ready only
        var readyPage = await fixture.Service.GetAll(1, 10, station1, "Ready", null, null, default);
        Assert.Equal(1, readyPage.TotalCount);
        Assert.Equal(task3.KitchenTaskId, readyPage.Items[0].KitchenTaskId);

        // All tasks
        var allPage = await fixture.Service.GetAll(1, 10, station1, "all", null, null, default);
        Assert.Equal(3, allPage.TotalCount);
    }

    [Fact]
    public async Task SummaryReturnsAccurateMetrics()
    {
        using var fixture = new Fixture();
        var (_, station) = await fixture.SeedTaskAsync("Pending", 0);
        var (task2, _) = await fixture.SeedTaskAsync("Pending", 0, station);

        // Cook task 2 for 8 minutes
        await fixture.Service.SetStatus(task2.KitchenTaskId, "Cooking", null, default);
        fixture.Clock.Advance(TimeSpan.FromMinutes(8));
        await fixture.Service.SetStatus(task2.KitchenTaskId, "Ready", null, default);

        var summary = await fixture.Service.GetSummary(station, default);
        Assert.Equal(1, summary.PendingCount);
        Assert.Equal(0, summary.CookingCount);
        Assert.Equal(1, summary.ReadyCount);
        Assert.Equal(8.0, summary.AvgCookingTimeMinutes);
    }

    private sealed class Access : IKitchenStationAccess
    {
        public bool Denied { get; set; }
        public Guid UserId { get; set; } = Guid.Parse("10000000-0000-0000-0000-000000000005");

        public Task RequireAsync(bool write, CancellationToken ct)
        {
            if (Denied)
            {
                throw new OrderingException(403, "Forbidden");
            }
            return Task.CompletedTask;
        }

        public Task<Guid> RequireKitchenTaskAccessAsync(CancellationToken ct)
        {
            if (Denied)
            {
                throw new OrderingException(403, "Forbidden");
            }
            return Task.FromResult(UserId);
        }
    }

    private sealed class TestTimeProvider : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UtcNow;

        public override DateTimeOffset GetUtcNow()
        {
            return _now;
        }

        public void Advance(TimeSpan duration)
        {
            _now = _now.Add(duration);
        }
    }

    private sealed class Fixture : IDisposable
    {
        private readonly SqliteConnection connection = new("Data Source=:memory:");
        public OrderingDbContext Db { get; }
        public Access Access { get; } = new();
        public TestTimeProvider Clock { get; } = new();
        public KitchenTaskService Service { get; }

        public Fixture()
        {
            connection.Open();
            Db = new TestDb(new DbContextOptionsBuilder<OrderingDbContext>()
                .UseSqlite(connection).Options);
            Db.Database.EnsureCreated();
            Service = new KitchenTaskService(
                new KitchenTaskRepository(Db),
                new OrderingTransaction(new UnitOfWork<OrderingDbContext>(Db)),
                Access,
                Clock);
        }

        public async Task<(KitchenTask Task, Guid StationId)> SeedTaskAsync(
            string status,
            int priority,
            Guid? existingStation = null)
        {
            var stationId = existingStation ?? Guid.NewGuid();
            if (existingStation is null)
            {
                Db.KitchenStations.Add(new KitchenStation
                {
                    KitchenStationId = stationId,
                    KitchenStationCode = $"ST-{stationId.ToString("N")[..4].ToUpperInvariant()}",
                    KitchenStationName = "Test Kitchen Station",
                    IsActive = true
                });
            }

            var orderId = Guid.NewGuid();
            var order = new Order
            {
                OrderId = orderId,
                OrderNumber = $"ORD-{orderId.ToString("N")[..6].ToUpperInvariant()}",
                TableSessionRef = Guid.NewGuid(),
                Channel = "DineIn",
                Status = "Confirmed",
                IdempotencyKey = Guid.NewGuid().ToString("N"),
                RequestHash = "seed-hash",
                CreatedAt = Clock.GetUtcNow().UtcDateTime,
                Version = new byte[8]
            };
            Db.Orders.Add(order);

            var item = new OrderItem
            {
                OrderItemId = Guid.NewGuid(),
                OrderId = orderId,
                ItemRef = Guid.NewGuid(),
                NameSnapshot = "Pho Bo Dac Biet",
                UnitPriceSnapshot = 75000,
                Quantity = 2,
                Note = "It hanh",
                Status = status,
                Version = new byte[8]
            };
            Db.OrderItems.Add(item);

            var task = new KitchenTask
            {
                KitchenTaskId = Guid.NewGuid(),
                KitchenStationId = stationId,
                OrderItemId = item.OrderItemId,
                OrderItem = item,
                BatchNumber = 1,
                Quantity = item.Quantity,
                Status = status,
                Priority = priority,
                QueuedAt = Clock.GetUtcNow().UtcDateTime,
                Version = new byte[8]
            };
            Db.KitchenTasks.Add(task);
            await Db.SaveChangesAsync();
            return (task, stationId);
        }

        public void Dispose()
        {
            Db.Dispose();
            connection.Dispose();
        }
    }

    private sealed class TestDb(DbContextOptions<OrderingDbContext> options) : OrderingDbContext(options)
    {
        private long version;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<Order>().Property(entity => entity.Version).ValueGeneratedNever();
            modelBuilder.Entity<OrderItem>().Property(entity => entity.Version).ValueGeneratedNever();
            modelBuilder.Entity<KitchenTask>().Property(entity => entity.Version).ValueGeneratedNever();
        }

        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            foreach (var entry in ChangeTracker.Entries().Where(entry => entry.State is EntityState.Added or EntityState.Modified))
            {
                if (entry.Entity is Order or OrderItem or KitchenTask)
                {
                    entry.Property("Version").CurrentValue = BitConverter.GetBytes(++version);
                }
            }

            return base.SaveChangesAsync(cancellationToken);
        }
    }
}
