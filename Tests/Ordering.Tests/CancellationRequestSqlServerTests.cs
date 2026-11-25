using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
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

public sealed class CancellationSqlFactAttribute : FactAttribute
{
    public CancellationSqlFactAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ORDERING_SQL_TEST_SERVER")))
        {
            Skip = "Set ORDERING_SQL_TEST_SERVER to run isolated SQL Server tests.";
        }
    }
}

public sealed partial class CancellationRequestSqlServerTests : IAsyncLifetime
{
    private readonly Guid restaurant = Guid.NewGuid();
    private readonly Guid session = Guid.NewGuid();
    private readonly Guid itemId = Guid.NewGuid();
    private readonly Guid guest = Guid.NewGuid();
    private readonly Guid waiter = Guid.NewGuid();
    private string? connectionString;
    private const string Prefix = "CodexCancellation_";

    public async Task InitializeAsync()
    {
        var server = Environment.GetEnvironmentVariable("ORDERING_SQL_TEST_SERVER");
        if (string.IsNullOrEmpty(server))
        {
            return;
        }

        var name = Prefix + Guid.NewGuid().ToString("N");
        var builder = new SqlConnectionStringBuilder
        {
            DataSource = server,
            InitialCatalog = "master",
            IntegratedSecurity = true,
            TrustServerCertificate = true
        };
        await using (var master = new SqlConnection(builder.ConnectionString))
        {
            await master.OpenAsync();
            await using var create = new SqlCommand($"CREATE DATABASE [{name}]", master);
            await create.ExecuteNonQueryAsync();
        }

        builder.InitialCatalog = name;
        connectionString = builder.ConnectionString;
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "docker", "sql", "schema", "OrderingDb.sql")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        var script = await File.ReadAllTextAsync(Path.Combine(directory!.FullName, "docker", "sql", "schema", "OrderingDb.sql"));
        await using (var connection = new SqlConnection(connectionString))
        {
            await connection.OpenAsync();
            foreach (var batch in Regex.Split(script, @"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(batch))
                {
                    continue;
                }

                await using var command = new SqlCommand(batch, connection);
                await command.ExecuteNonQueryAsync();
            }
        }

        await using var db = Db();
        var order = new Order
        {
            OrderId = Guid.NewGuid(),
            OrderNumber = "TEST",
            TableSessionRef = session,
            RestaurantRef = restaurant,
            Channel = "DineIn",
            Status = "Confirmed",
            IdempotencyKey = Guid.NewGuid().ToString(),
            RequestHash = "seed",
            CreatedAt = DateTime.UtcNow
        };
        var item = new OrderItem
        {
            OrderItemId = itemId,
            Order = order,
            ItemRef = Guid.NewGuid(),
            OriginalTableRef = Guid.NewGuid(),
            NameSnapshot = "Test dish",
            Quantity = 2,
            Status = "Pending",
            UnitPriceSnapshot = 50000
        };
        db.KitchenTasks.Add(new KitchenTask
        {
            KitchenTaskId = Guid.NewGuid(),
            KitchenStation = new KitchenStation
            {
                KitchenStationId = Guid.NewGuid(),
                KitchenStationCode = "HOT",
                KitchenStationName = "Hot kitchen",
                IsActive = true
            },
            OrderItem = item,
            BatchNumber = 1,
            Quantity = 2,
            Status = "Pending",
            QueuedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
    }

    [CancellationSqlFact]
    public async Task ConcurrentRequestsAndDecisionsHaveOneWinnerAndPreserveUnicode()
    {
        var requestIds = new[] { Guid.NewGuid(), Guid.NewGuid() };
        var created = await Task.WhenAll(requestIds.Select(async id =>
        {
            await using var db = Db();
            return await Status(async () =>
            {
                await Service(db).Create(session, id, itemId, 1, "Khách đặt dư món", "guest", default);
            });
        }));
        Assert.Single(created, code => code == 200);
        Assert.Single(created, code => code == 409);
        Guid requestId;
        await using (var db = Db())
        {
            var request = await db.CancellationRequests.SingleAsync();
            Assert.Equal("Khách đặt dư món", request.Reason);
            requestId = request.CancellationRequestId;
        }

        var decisions = await Task.WhenAll(new[] { true, false }.Select(async approve =>
        {
            await using var db = Db();
            return await Status(async () =>
            {
                if (approve)
                {
                    await Service(db).Approve(session, requestId, default);
                }
                else
                {
                    await Service(db).Reject(session, requestId, "Khách giữ món", default);
                }
            });
        }));
        Assert.Single(decisions, code => code == 200);
        Assert.Single(decisions, code => code == 409);
        await using var final = Db();
        var decided = await final.CancellationRequests.SingleAsync();
        var item = await final.OrderItems.SingleAsync();
        Assert.Equal(decided.Status == "Approved" ? 1 : 0, item.CancelledQuantity);
        Assert.Equal(decided.Status == "Approved" ? 1 : 2, (await final.KitchenTasks.SingleAsync()).Quantity);
        Assert.NotNull(decided.DecidedAt);
    }

    [CancellationSqlFact]
    public async Task OtherRestaurantCannotReadCreateOrDecide()
    {
        var id = Guid.NewGuid();
        await using (var db = Db())
        {
            await Service(db).Create(session, id, itemId, 1, "Cancel", "guest", default);
        }

        await using var foreign = Db(Guid.NewGuid());
        var service = Service(foreign);
        Assert.Empty((await service.GetAll(null, null, 1, 20, "", default)).Items);
        Assert.Equal(404, await Status(async () =>
        {
            await service.GetById(session, id, "guest", default);
        }));
        Assert.Equal(404, await Status(async () =>
        {
            await service.Create(session, Guid.NewGuid(), itemId, 1, "Cancel", "guest", default);
        }));
        Assert.Equal(404, await Status(async () =>
        {
            await service.Approve(session, id, default);
        }));
        Assert.Equal(404, await Status(async () =>
        {
            await service.CancelByStaff(session, Guid.NewGuid(), itemId, 1, "Reason", default);
        }));
    }

    [CancellationSqlFact]
    public async Task CheckoutAndCancellationCannotBothWin()
    {
        var result = await Task.WhenAll(Request(), Checkout());
        Assert.Single(result, code => code == 200);
        Assert.Single(result, code => code == 409);

        async Task<int> Request()
        {
            await using var db = Db();
            return await Status(async () =>
            {
                await Service(db).Create(session, Guid.NewGuid(), itemId, 1, "Cancel", "guest", default);
            });
        }

        async Task<int> Checkout()
        {
            await using var db = Db();
            var service = new CheckoutLockService(new CheckoutLockRepository(db), new OrderRepository(db),
                new OrderingTransaction(new UnitOfWork<OrderingDbContext>(db)), new Access(session, guest, waiter),
                new Access(session, guest, waiter), TimeProvider.System);
            return await Status(async () =>
            {
                await service.Begin(session, Guid.NewGuid(), "", default);
            });
        }
    }

    [CancellationSqlFact]
    public Task StartingKitchenAndCancellationCannotBothWin()
    {
        return AssertKitchenRace(false);
    }

    [CancellationSqlFact]
    public Task StartingBatchAndCancellationCannotBothWin()
    {
        return AssertKitchenRace(true);
    }

    private async Task AssertKitchenRace(bool batch)
    {
        Guid taskId;
        await using (var db = Db())
        {
            taskId = (await db.KitchenTasks.SingleAsync()).KitchenTaskId;
        }

        var inventory = new KitchenTaskTests.FakeCookingInventoryClient();
        var result = await Task.WhenAll(Request(), Cook());
        Assert.Single(result, code => code == 200);
        Assert.Single(result, code => code == 409);
        await using var final = Db();
        var task = await final.KitchenTasks.SingleAsync();
        Assert.Equal(task.Status == "Cooking" ? 1 : 0, inventory.Calls.Count);
        Assert.Equal(task.Status == "Cooking" ? 0 : 1, await final.CancellationRequests.CountAsync());

        async Task<int> Request()
        {
            await using var db = Db();
            return await Status(async () =>
            {
                await Service(db).Create(session, Guid.NewGuid(), itemId, 1, "Cancel", "guest", default);
            });
        }

        async Task<int> Cook()
        {
            await using var db = Db();
            var kitchen = new KitchenTaskService(new KitchenTaskRepository(db),
                new OrderingTransaction(new UnitOfWork<OrderingDbContext>(db)), new KitchenAccess(),
                inventory, TimeProvider.System, new CheckoutLockRepository(db));
            return await Status(async () =>
            {
                if (batch)
                {
                    await kitchen.StartBatch([taskId], default);
                }
                else
                {
                    await kitchen.SetStatus(taskId, "Cooking", null, default);
                }
            });
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

    [CancellationSqlFact]
    public async Task StaffCancellationReplayCannotDoubleReduceQuantity()
    {
        var id = Guid.NewGuid();
        var result = await Task.WhenAll(Enumerable.Range(0, 2).Select(async _ =>
        {
            await using var db = Db();
            return await Service(db).CancelByStaff(session, id, itemId, 1, "Hết nguyên liệu", default);
        }));
        Assert.Single(result, value => value.Created);
        Assert.Single(result, value => !value.Created);
        await using var final = Db();
        Assert.Equal(1, (await final.OrderItems.SingleAsync()).CancelledQuantity);
        Assert.Equal(1, (await final.KitchenTasks.SingleAsync()).Quantity);
        var history = await final.CancellationRequests.SingleAsync();
        Assert.Equal("Staff", history.Source);
        Assert.Equal("Hết nguyên liệu", history.Reason);
    }

    [CancellationSqlFact]
    public async Task ConcurrentFullStaffCancellationsCannotOverCancel()
    {
        var result = await Task.WhenAll(Enumerable.Range(0, 2).Select(async _ =>
        {
            await using var db = Db();
            return await Status(async () =>
            {
                await Service(db).CancelByStaff(session, Guid.NewGuid(), itemId, 2, "Unavailable", default);
            });
        }));
        Assert.Single(result, code => code == 200);
        Assert.Single(result, code => code == 409);
        await using var final = Db();
        Assert.Equal(2, (await final.OrderItems.SingleAsync()).CancelledQuantity);
        Assert.Equal("Cancelled", (await final.KitchenTasks.SingleAsync()).Status);
        Assert.Equal(1, await final.CancellationRequests.CountAsync());
    }

    private OrderingDbContext Db(Guid? tenant = null)
    {
        var scope = new RestaurantDataScope();
        scope.Select(tenant ?? restaurant);
        return new OrderingDbContext(new DbContextOptionsBuilder<OrderingDbContext>().UseSqlServer(connectionString).Options, scope);
    }

    private CancellationRequestService Service(OrderingDbContext db)
    {
        var access = new Access(session, guest, waiter);
        return new CancellationRequestService(new CancellationRequestRepository(db), new CheckoutLockRepository(db),
            new OrderingTransaction(new UnitOfWork<OrderingDbContext>(db)), access, access, TimeProvider.System);
    }

    private static async Task<int> Status(Func<Task> action)
    {
        try
        {
            await action();
            return 200;
        }
        catch (OrderingException error)
        {
            return error.StatusCode;
        }
    }

    private sealed class Access(Guid session, Guid guest, Guid waiter) : IOrderingAccess,
        ICancellationRequestStaffAccess, ICheckoutAccess, ICheckoutReleaseAuthorization
    {
        public Task<OrderingActor> RequireAsync(Guid sessionId, Guid? tableId, string guestToken, CancellationToken ct)
        {
            Assert.Equal(session, sessionId);
            return Task.FromResult(new OrderingActor(sessionId, tableId,
                guestToken.Length == 0 ? waiter : null, guestToken.Length == 0 ? null : guest, DateTimeOffset.UtcNow.AddHours(1)));
        }

        public Task<OrderingActor> RequireForServingAsync(Guid sessionId, CancellationToken ct)
        {
            return RequireAsync(sessionId, null, "", ct);
        }

        public Task RequireAsync(CancellationToken ct)
        {
            return Task.CompletedTask;
        }

        public Task RequireAsync(Guid sessionId, string guestToken, bool write, CancellationToken ct)
        {
            return Task.CompletedTask;
        }

        public Task RequireAsync(Guid sessionId, Guid checkoutId, CancellationToken ct)
        {
            return Task.CompletedTask;
        }
    }

    public async Task DisposeAsync()
    {
        if (connectionString is null)
        {
            return;
        }

        var name = new SqlConnectionStringBuilder(connectionString).InitialCatalog;
        Assert.StartsWith(Prefix, name);
        Assert.True(Guid.TryParseExact(name[Prefix.Length..], "N", out _));
        await using var db = Db();
        await db.Database.EnsureDeletedAsync();
    }
}
