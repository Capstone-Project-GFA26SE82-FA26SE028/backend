using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Ordering.BLL.Implements;
using Ordering.BLL.Interfaces;
using Ordering.Common.DTOs;
using Ordering.Common.Exceptions;
using Ordering.DAL.Implements;
using Ordering.DAL.Models;
using Shared.Persistence.RestaurantIsolation;
using Shared.Persistence.UnitOfWorks;
using Xunit;

namespace Ordering.Tests;

public sealed class KitchenAssignmentTests
{
    [Fact]
    public async Task CreateReadReplaceDeleteAndRejectStaleChanges()
    {
        using var f = new Fixture();
        var station = await f.Station();
        var first = f.Access.Employees[0].UserId;
        var second = f.Access.Employees[1].UserId;
        var created = await f.Service.Create(station, first, default);
        Assert.Equal(first, created.Employee!.UserId);
        Assert.Equal(409, (await Assert.ThrowsAsync<OrderingException>(() => f.Service.Create(station, second, default))).StatusCode);
        Assert.Equal(1, (await f.Service.GetAll(1, 20, default)).TotalCount);
        await f.Service.Update(station, second, first, default);
        Assert.Equal(second, (await f.Service.GetById(station, default)).UserId);
        Assert.Equal(409, (await Assert.ThrowsAsync<OrderingException>(() => f.Service.Delete(station, first, default))).StatusCode);
        await f.Service.Delete(station, second, default);
        Assert.Empty((await f.Service.GetAll(1, 20, default)).Items);
    }

    [Theory]
    [InlineData("Pending", true)]
    [InlineData("Cooking", false)]
    public async Task UnfinishedTasksBlockRemovalAndCookingBlocksReplacement(string status, bool canReplace)
    {
        using var f = new Fixture();
        var station = await f.Station();
        var first = f.Access.Employees[0].UserId;
        var second = f.Access.Employees[1].UserId;
        await f.Service.Create(station, first, default);
        await f.Task(station, status);
        Assert.Equal(409, (await Assert.ThrowsAsync<OrderingException>(() => f.Service.Delete(station, first, default))).StatusCode);
        if (canReplace)
        {
            await f.Service.Update(station, second, first, default);
        }
        else
        {
            Assert.Equal(409, (await Assert.ThrowsAsync<OrderingException>(() => f.Service.Update(station, second, first, default))).StatusCode);
        }
    }

    [Fact]
    public async Task OtherRestaurantStationIsHiddenAndCannotBeAssigned()
    {
        using var f = new Fixture();
        var foreign = Guid.NewGuid();
        using (var seed = new OrderingDbContext(new DbContextOptionsBuilder<OrderingDbContext>().UseSqlite(f.Connection).Options))
        {
            var station = new KitchenStation
            {
                KitchenStationId = foreign,
                KitchenStationCode = "OTHER",
                KitchenStationName = "Other restaurant",
                IsActive = true
            };
            seed.Add(station);
            seed.Entry(station).Property("RestaurantId").CurrentValue = Guid.NewGuid();
            await seed.SaveChangesAsync();
        }

        Assert.Equal(404, (await Assert.ThrowsAsync<OrderingException>(() => f.Service.Create(foreign, f.Access.Employees[0].UserId, default))).StatusCode);
        Assert.Empty((await f.Service.GetAll(1, 20, default)).Items);
    }

    [Fact]
    public async Task UnauthorizedManagerAndIneligibleEmployeeCannotWrite()
    {
        using var f = new Fixture();
        var station = await f.Station();
        f.Access.Denied = true;
        Assert.Equal(403, (await Assert.ThrowsAsync<OrderingException>(() => f.Service.Create(station, f.Access.Employees[0].UserId, default))).StatusCode);
        f.Access.Denied = false;
        Assert.Equal(409, (await Assert.ThrowsAsync<OrderingException>(() => f.Service.Create(station, Guid.NewGuid(), default))).StatusCode);
        f.Access.Employees[0].IsActive = false;
        Assert.Equal(409, (await Assert.ThrowsAsync<OrderingException>(() => f.Service.Create(station, f.Access.Employees[0].UserId, default))).StatusCode);
        Assert.Single(await f.Service.GetEmployees(default));
        Assert.Empty((await f.Service.GetAll(1, 20, default)).Items);
    }

    [Fact]
    public async Task DisabledStationAndInvalidPaginationRejected()
    {
        using var f = new Fixture();
        var station = await f.Station(false);
        Assert.Equal(409, (await Assert.ThrowsAsync<OrderingException>(() => f.Service.Create(station, f.Access.Employees[0].UserId, default))).StatusCode);
        Assert.Equal(400, (await Assert.ThrowsAsync<OrderingException>(() => f.Service.GetAll(int.MaxValue, 100, default))).StatusCode);
    }

    private sealed class Access : IKitchenAssignmentAccess
    {
        public bool Denied { get; set; }
        public List<KitchenEmployeeDTO> Employees { get; } =
        [
            new()
            {
                UserId = Guid.NewGuid(),
                RoleId = "kitchen",
                IsActive = true
            },
            new()
            {
                UserId = Guid.NewGuid(),
                RoleId = "kitchen",
                IsActive = true
            }
        ];

        public Task RequireManager(CancellationToken ct)
        {
            if (Denied)
            {
                throw new OrderingException(403, "Denied");
            }

            return System.Threading.Tasks.Task.CompletedTask;
        }

        public Task<IReadOnlyList<KitchenEmployeeDTO>> GetEmployees(CancellationToken ct)
        {
            return System.Threading.Tasks.Task.FromResult<IReadOnlyList<KitchenEmployeeDTO>>(Employees);
        }

        public Task RequireEmployee(Guid id, CancellationToken ct)
        {
            if (!Employees.Any(item => item.UserId == id && item.IsActive && item.RoleId == "kitchen"))
            {
                throw new OrderingException(409, "Ineligible");
            }

            return System.Threading.Tasks.Task.CompletedTask;
        }
    }

    private sealed class Fixture : IDisposable
    {
        public SqliteConnection Connection { get; } = new("Data Source=:memory:");
        public TestDb Db { get; }
        public Access Access { get; } = new();
        public KitchenAssignmentService Service { get; }

        public Fixture()
        {
            Connection.Open();
            var scope = new RestaurantDataScope();
            scope.Select(Guid.NewGuid());
            Db = new TestDb(new DbContextOptionsBuilder<OrderingDbContext>().UseSqlite(Connection).Options, scope);
            Db.Database.EnsureCreated();
            Service = new KitchenAssignmentService(new KitchenAssignmentRepository(Db),
                new OrderingTransaction(new UnitOfWork<OrderingDbContext>(Db)), Access);
        }

        public async Task<Guid> Station(bool active = true)
        {
            var station = new KitchenStation
            {
                KitchenStationId = Guid.NewGuid(),
                KitchenStationCode = "HOT",
                KitchenStationName = "Hot kitchen",
                IsActive = active
            };
            Db.Add(station);
            await Db.SaveChangesAsync();
            return station.KitchenStationId;
        }

        public async Task Task(Guid station, string status)
        {
            var order = new Order
            {
                OrderId = Guid.NewGuid(),
                OrderNumber = Guid.NewGuid().ToString("N"),
                TableSessionRef = Guid.NewGuid(),
                Channel = "DineIn",
                Status = "Confirmed",
                IdempotencyKey = Guid.NewGuid().ToString(),
                RequestHash = "seed",
                CreatedAt = DateTime.UtcNow,
                Version = new byte[8]
            };
            var item = new OrderItem
            {
                OrderItemId = Guid.NewGuid(),
                Order = order,
                ItemRef = Guid.NewGuid(),
                NameSnapshot = "Dish",
                Quantity = 1,
                Status = status,
                Version = new byte[8]
            };
            Db.Add(new KitchenTask
            {
                KitchenTaskId = Guid.NewGuid(),
                KitchenStationId = station,
                OrderItem = item,
                Status = status,
                BatchNumber = 1,
                Quantity = 1,
                QueuedAt = DateTime.UtcNow,
                Version = new byte[8]
            });
            await Db.SaveChangesAsync();
        }

        public void Dispose()
        {
            Db.Dispose();
            Connection.Dispose();
        }
    }

    private sealed class TestDb(DbContextOptions<OrderingDbContext> options, RestaurantDataScope scope)
        : OrderingDbContext(options, scope)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<Order>().Property(item => item.Version).ValueGeneratedNever();
            modelBuilder.Entity<OrderItem>().Property(item => item.Version).ValueGeneratedNever();
            modelBuilder.Entity<KitchenTask>().Property(item => item.Version).ValueGeneratedNever();
        }
    }
}
