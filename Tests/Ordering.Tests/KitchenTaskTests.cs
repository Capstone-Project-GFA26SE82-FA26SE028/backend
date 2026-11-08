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
    public async Task FrozenCheckoutBlocksCancellationButAllowsCookingAndServingProgress()
    {
        using var fixture = new Fixture();
        var (task, _) = await fixture.SeedTaskAsync("Pending", 0);
        fixture.Db.CheckoutLocks.Add(new CheckoutLock
        {
            CheckoutId = Guid.NewGuid(),
            TableSessionRef = task.OrderItem.Order.TableSessionRef!.Value,
            Status = "Frozen",
            CreatedAt = DateTime.UtcNow,
            SourceHash = new string('A', 64)
        });
        await fixture.Db.SaveChangesAsync();
        var error = await Assert.ThrowsAsync<OrderingException>(() => fixture.Service.SetStatus(task.KitchenTaskId,
            "Cancelled", null, default));
        Assert.Equal(409, error.StatusCode);
        await fixture.Service.SetStatus(task.KitchenTaskId, "Cooking", null, default);
        var ready = await fixture.Service.SetStatus(task.KitchenTaskId, "Ready", null, default);
        Assert.Equal("Ready", ready.Status);
    }

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

    [Fact]
    public async Task BatchesAreGroupedByDishAndStationAndCanBeCookedTogether()
    {
        using var fixture = new Fixture();
        var (_, station) = await fixture.SeedTaskAsync("Pending", 0);
        var phoItemId = Guid.NewGuid();
        var bunChaItemId = Guid.NewGuid();

        var (taskPho1, _) = await fixture.SeedTaskCustomAsync("Pending", 50, station, phoItemId, "Pho Bo Dac Biet", null, 2);
        var (taskPho2, _) = await fixture.SeedTaskCustomAsync("Pending", 50, station, phoItemId, "Pho Bo Dac Biet", null, 3);
        var (taskBunCha, _) = await fixture.SeedTaskCustomAsync("Pending", 50, station, bunChaItemId, "Bun Cha Ha Noi", null, 1);

        var batches = await fixture.Service.GetBatches(station, default);
        Assert.Equal(3, batches.Count);

        var phoBatch = batches.Single(b => b.ItemId == phoItemId);
        Assert.Equal(5, phoBatch.TotalQuantity);
        Assert.Equal(2, phoBatch.Tasks.Count);

        var (startCount, startStatus) = await fixture.Service.StartBatch(
            new List<Guid> { taskPho1.KitchenTaskId, taskPho2.KitchenTaskId },
            default);
        Assert.Equal(2, startCount);
        Assert.Equal("Cooking", startStatus);

        var dbTask1 = await fixture.Db.KitchenTasks.FirstAsync(t => t.KitchenTaskId == taskPho1.KitchenTaskId);
        var dbTask2 = await fixture.Db.KitchenTasks.FirstAsync(t => t.KitchenTaskId == taskPho2.KitchenTaskId);
        Assert.Equal("Cooking", dbTask1.Status);
        Assert.Equal("Cooking", dbTask2.Status);

        var (completeCount, completeStatus) = await fixture.Service.CompleteBatch(
            new List<Guid> { taskPho1.KitchenTaskId, taskPho2.KitchenTaskId },
            default);
        Assert.Equal(2, completeCount);
        Assert.Equal("Ready", completeStatus);

        dbTask1 = await fixture.Db.KitchenTasks.FirstAsync(t => t.KitchenTaskId == taskPho1.KitchenTaskId);
        dbTask2 = await fixture.Db.KitchenTasks.FirstAsync(t => t.KitchenTaskId == taskPho2.KitchenTaskId);
        Assert.Equal("Ready", dbTask1.Status);
        Assert.Equal("Ready", dbTask2.Status);
    }

    [Fact]
    public async Task RemakeTaskCreatesPriority999Ticket()
    {
        using var fixture = new Fixture();
        var (readyTask, _) = await fixture.SeedTaskAsync("Ready", 50);

        var remake = await fixture.Service.RemakeTask(readyTask.KitchenTaskId, "Món bị nguội, khách yêu cầu làm lại", default);
        Assert.Equal("Pending", remake.Status);
        Assert.Equal(999, remake.Priority);

        var tasks = await fixture.Db.KitchenTasks.Where(t => t.OrderItemId == readyTask.OrderItemId).ToListAsync();
        Assert.Equal(2, tasks.Count);

        var newTicket = tasks.Single(t => t.KitchenTaskId == remake.KitchenTaskId);
        Assert.Equal(999, newTicket.Priority);
        Assert.Equal("Pending", newTicket.Status);

        var orderItem = await fixture.Db.OrderItems.FirstAsync(i => i.OrderItemId == readyTask.OrderItemId);
        Assert.Equal("Pending", orderItem.Status);
    }

    [Fact]
    public async Task HoldAndFireActivatesDessertCourses()
    {
        using var fixture = new Fixture();
        var (_, station) = await fixture.SeedTaskAsync("Pending", 0);
        var sessionId = Guid.NewGuid();

        var (dessertTask1, _) = await fixture.SeedTaskCustomAsync("Hold", 20, station, null, "Chè Thái Sầu Riêng", sessionId, 1);
        var (dessertTask2, _) = await fixture.SeedTaskCustomAsync("Hold", 20, station, null, "Kem Trái Dừa", sessionId, 2);

        var result = await fixture.Service.FireCourse(sessionId, null, "Dessert", default);
        Assert.Equal(2, result.ActivatedCount);

        var dbTask1 = await fixture.Db.KitchenTasks.FirstAsync(t => t.KitchenTaskId == dessertTask1.KitchenTaskId);
        var dbTask2 = await fixture.Db.KitchenTasks.FirstAsync(t => t.KitchenTaskId == dessertTask2.KitchenTaskId);
        Assert.Equal("Pending", dbTask1.Status);
        Assert.Equal("Pending", dbTask2.Status);

        var item1 = await fixture.Db.OrderItems.FirstAsync(i => i.OrderItemId == dessertTask1.OrderItemId);
        var item2 = await fixture.Db.OrderItems.FirstAsync(i => i.OrderItemId == dessertTask2.OrderItemId);
        Assert.Equal("Pending", item1.Status);
        Assert.Equal("Pending", item2.Status);
    }

    [Fact]
    public async Task CancelCookingOrReadyTaskThrowsOrderingException()
    {
        using var fixture = new Fixture();
        var (cookingTask, _) = await fixture.SeedTaskAsync("Cooking", 50);

        var ex1 = await Assert.ThrowsAsync<OrderingException>(() =>
            fixture.Service.SetStatus(cookingTask.KitchenTaskId, "Cancelled", null, default));
        Assert.Equal(400, ex1.StatusCode);

        var (readyTask, _) = await fixture.SeedTaskAsync("Ready", 50);
        var ex2 = await Assert.ThrowsAsync<OrderingException>(() =>
            fixture.Service.SetStatus(readyTask.KitchenTaskId, "Cancelled", null, default));
        Assert.Equal(400, ex2.StatusCode);
    }

    [Fact]
    public async Task EstimatedCookingTimeAndLateAlertCalculatedCorrectly()
    {
        using var fixture = new Fixture();
        var (task, _) = await fixture.SeedTaskAsync("Pending", 0);

        var initialDto = await fixture.Service.GetById(task.KitchenTaskId, default);
        Assert.Null(initialDto.EstimatedMinutes);
        Assert.Null(initialDto.DueAt);
        Assert.False(initialDto.IsLate);

        var dbTask = await fixture.Db.KitchenTasks.FirstAsync(t => t.KitchenTaskId == task.KitchenTaskId);
        dbTask.EstimatedMinutes = 15;
        await fixture.Db.SaveChangesAsync();

        var pendingDto = await fixture.Service.GetById(task.KitchenTaskId, default);
        Assert.Equal(15, pendingDto.EstimatedMinutes);
        Assert.Null(pendingDto.DueAt);
        Assert.False(pendingDto.IsLate);

        var cookingDto = await fixture.Service.SetStatus(task.KitchenTaskId, "Cooking", null, default);
        Assert.NotNull(cookingDto.CookingAt);
        Assert.NotNull(cookingDto.DueAt);
        Assert.Equal(cookingDto.CookingAt.Value.AddMinutes(15), cookingDto.DueAt.Value);
        Assert.False(cookingDto.IsLate);

        fixture.Clock.Advance(TimeSpan.FromMinutes(10));
        var inProgressDto = await fixture.Service.GetById(task.KitchenTaskId, default);
        Assert.False(inProgressDto.IsLate);

        fixture.Clock.Advance(TimeSpan.FromMinutes(6));
        var lateDto = await fixture.Service.GetById(task.KitchenTaskId, default);
        Assert.True(lateDto.IsLate);

        var readyDto = await fixture.Service.SetStatus(task.KitchenTaskId, "Ready", null, default);
        Assert.False(readyDto.IsLate);
        Assert.NotNull(readyDto.ReadyAt);
    }

    [Fact]
    public async Task SetStatus_ToCooking_DeductsInventorySuccessfully()
    {
        using var fixture = new Fixture();
        var (task, _) = await fixture.SeedTaskAsync("Pending", 1);

        var result = await fixture.Service.SetStatus(task.KitchenTaskId, "Cooking", null, default);

        Assert.Equal("Cooking", result.Status);
        Assert.Single(fixture.CookingClient.Calls);
        var call = fixture.CookingClient.Calls[0];
        Assert.NotEqual(Guid.Empty, call.EventId);
        Assert.Single(call.Tasks);
        Assert.Equal(task.KitchenTaskId, call.Tasks[0].KitchenTaskId);
    }

    [Fact]
    public async Task SetStatus_ToCooking_InsufficientStock_Throws409AndTaskRemainsPending()
    {
        using var fixture = new Fixture();
        var (task, _) = await fixture.SeedTaskAsync("Pending", 1);
        fixture.CookingClient.ShouldThrowInsufficientStock = true;
        fixture.CookingClient.ErrorMessage = "Nguyên liệu 'Thịt bò' không đủ tồn kho khả dụng để chế biến món.";

        var ex = await Assert.ThrowsAsync<OrderingException>(() =>
            fixture.Service.SetStatus(task.KitchenTaskId, "Cooking", null, default));

        Assert.Equal(409, ex.StatusCode);
        Assert.Contains("không đủ tồn kho", ex.Message);

        var taskInDb = await fixture.Db.KitchenTasks.FirstAsync(t => t.KitchenTaskId == task.KitchenTaskId);
        Assert.Equal("Pending", taskInDb.Status);
        Assert.Null(taskInDb.CookingAt);
    }

    [Fact]
    public async Task StartBatch_DeductsInventoryForBatchAndStartsCooking()
    {
        using var fixture = new Fixture();
        var (task1, stationId) = await fixture.SeedTaskAsync("Pending", 1);
        var (task2, _) = await fixture.SeedTaskAsync("Pending", 2, stationId);

        var (count, status) = await fixture.Service.StartBatch([task1.KitchenTaskId, task2.KitchenTaskId], default);

        Assert.Equal(2, count);
        Assert.Equal("Cooking", status);
        Assert.Single(fixture.CookingClient.Calls);
        var call = fixture.CookingClient.Calls[0];
        Assert.Equal(2, call.Tasks.Count);
        Assert.Contains(call.Tasks, t => t.KitchenTaskId == task1.KitchenTaskId);
        Assert.Contains(call.Tasks, t => t.KitchenTaskId == task2.KitchenTaskId);

        var dbTask1 = await fixture.Db.KitchenTasks.FirstAsync(t => t.KitchenTaskId == task1.KitchenTaskId);
        var dbTask2 = await fixture.Db.KitchenTasks.FirstAsync(t => t.KitchenTaskId == task2.KitchenTaskId);
        Assert.Equal("Cooking", dbTask1.Status);
        Assert.Equal("Cooking", dbTask2.Status);
    }

    [Fact]
    public async Task StartBatch_InsufficientStock_RollsBackAllTasks()
    {
        using var fixture = new Fixture();
        var (task1, stationId) = await fixture.SeedTaskAsync("Pending", 1);
        var (task2, _) = await fixture.SeedTaskAsync("Pending", 2, stationId);
        fixture.CookingClient.ShouldThrowInsufficientStock = true;

        var ex = await Assert.ThrowsAsync<OrderingException>(() =>
            fixture.Service.StartBatch([task1.KitchenTaskId, task2.KitchenTaskId], default));

        Assert.Equal(409, ex.StatusCode);

        var dbTask1 = await fixture.Db.KitchenTasks.FirstAsync(t => t.KitchenTaskId == task1.KitchenTaskId);
        var dbTask2 = await fixture.Db.KitchenTasks.FirstAsync(t => t.KitchenTaskId == task2.KitchenTaskId);
        Assert.Equal("Pending", dbTask1.Status);
        Assert.Equal("Pending", dbTask2.Status);
    }

    public sealed class FakeCookingInventoryClient : ICookingInventoryClient
    {
        public List<(Guid EventId, IReadOnlyList<KitchenTask> Tasks)> Calls { get; } = [];
        public bool ShouldThrowInsufficientStock { get; set; }
        public string ErrorMessage { get; set; } = "Insufficient stock.";

        public Task DeductCookingIngredientsAsync(
            Guid cookingEventId,
            IReadOnlyList<KitchenTask> tasks,
            CancellationToken ct)
        {
            if (ShouldThrowInsufficientStock)
            {
                throw new OrderingException(409, ErrorMessage);
            }

            Calls.Add((cookingEventId, tasks));
            return Task.CompletedTask;
        }
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

        public FakeCookingInventoryClient CookingClient { get; } = new();

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
                CookingClient,
                Clock,
                new CheckoutLockRepository(Db));
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

        public async Task<(KitchenTask Task, Guid StationId)> SeedTaskCustomAsync(
            string status,
            int priority,
            Guid? existingStation = null,
            Guid? itemId = null,
            string itemName = "Pho Bo Dac Biet",
            Guid? tableSessionId = null,
            int quantity = 2)
        {
            var stationId = existingStation ?? Guid.NewGuid();
            if (existingStation is null && !await Db.KitchenStations.AnyAsync(s => s.KitchenStationId == stationId))
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
                TableSessionRef = tableSessionId ?? Guid.NewGuid(),
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
                ItemRef = itemId ?? Guid.NewGuid(),
                NameSnapshot = itemName,
                UnitPriceSnapshot = 75000,
                Quantity = quantity,
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
