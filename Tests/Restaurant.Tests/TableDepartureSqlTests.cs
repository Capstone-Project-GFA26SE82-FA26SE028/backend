using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Restaurant.BLL.Implements;
using Restaurant.BLL.Interfaces;
using Restaurant.Common.DTOs;
using Restaurant.Common.Exceptions;
using Restaurant.Common.Settings;
using Restaurant.DAL.Implements;
using Restaurant.DAL.Models;
using Shared.Persistence.UnitOfWorks;
using Xunit;

namespace Restaurant.Tests;

public sealed class DepartureSqlFactAttribute : FactAttribute
{
    public DepartureSqlFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("RESTAURANT_DEPARTURE_SQL_CONNECTION")))
        {
            Skip = "Set RESTAURANT_DEPARTURE_SQL_CONNECTION to run isolated SQL Server departure tests.";
        }
    }
}

public sealed class TableDepartureSqlTests : IAsyncLifetime
{
    private readonly string _database = "CodexDeparture_" + Guid.NewGuid().ToString("N");
    private readonly Guid _restaurantId = Guid.NewGuid();
    private readonly Guid _sessionId = Guid.NewGuid();
    private readonly Guid _tableId = Guid.NewGuid();
    private readonly Guid _otherTableId = Guid.NewGuid();
    private readonly PaidClient _paid = new();
    private string _master = string.Empty;
    private string _connection = string.Empty;
    private bool _created;

    public async Task InitializeAsync()
    {
        var configured = Environment.GetEnvironmentVariable("RESTAURANT_DEPARTURE_SQL_CONNECTION");
        if (string.IsNullOrWhiteSpace(configured))
        {
            return;
        }

        var builder = new SqlConnectionStringBuilder(configured)
        {
            InitialCatalog = "master",
            Pooling = false
        };
        _master = builder.ConnectionString;
        builder.InitialCatalog = _database;
        _connection = builder.ConnectionString;
        await Execute(_master, $"CREATE DATABASE [{_database}];");
        _created = true;
        var schema = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Sql", "RestaurantDb.sql"));
        foreach (var batch in Regex.Split(schema, @"^\s*GO\s*$", RegexOptions.IgnoreCase | RegexOptions.Multiline))
        {
            if (!string.IsNullOrWhiteSpace(batch))
            {
                await Execute(_connection, batch);
            }
        }

        await using var db = Context();
        var area = new Area
        {
            AreaId = Guid.NewGuid(),
            AreaCode = "TEST",
            AreaName = "TEST",
            ServiceType = "DineIn",
            SpaceType = "Indoor",
            IsActive = true,
            Restaurant = new Restaurant.DAL.Models.Restaurant
            {
                RestaurantId = _restaurantId,
                RestaurantName = "Departure SQL fixture",
                Address = "Test",
                TimeZone = "UTC"
            }
        };
        var now = DateTime.UtcNow.AddHours(-1);
        db.TableSessions.Add(new TableSession
        {
            TableSessionId = _sessionId,
            Status = "Open",
            PartySize = 4,
            OpenedAt = now,
            SessionTables = new[] { _tableId, _otherTableId }.Select(id => new SessionTable
            {
                SessionTableId = Guid.NewGuid(),
                AssignedAt = now,
                Table = new Table
                {
                    TableId = id,
                    TableCode = id.ToString("N"),
                    Area = area,
                    Capacity = 2,
                    IsActive = true,
                    CleaningStatus = "Clean"
                }
            }).ToList(),
            GuestAccesses =
            [
                new GuestAccess
                {
                    GuestAccessId = Guid.NewGuid(),
                    TokenHash = new string('a', 64),
                    CreatedAt = now,
                    ExpiredAt = now.AddHours(12)
                }
            ]
        });
        await db.SaveChangesAsync();
    }

    [DepartureSqlFact]
    public async Task ConcurrentDeparturesReleaseAllTablesAndRevokeGuestsExactlyOnce()
    {
        await using var first = Context();
        await using var second = Context();
        var results = await Task.WhenAll(Sessions(first).Depart(_sessionId, default), Sessions(second).Depart(_sessionId, default));
        Assert.Equal(results[0].ClosedAt, results[1].ClosedAt);
        await using var check = Context();
        var session = await check.TableSessions.SingleAsync();
        Assert.Equal("Closed", session.Status);
        Assert.All(await check.SessionTables.ToListAsync(), link => Assert.Equal(session.ClosedAt, link.ReleasedAt));
        Assert.All(await check.Tables.ToListAsync(), table => Assert.Equal("Dirty", table.CleaningStatus));
        Assert.Equal(session.ClosedAt, (await check.GuestAccesses.SingleAsync()).RevokedAt);
        Assert.Equal(2, results[0].TableIds.Length);
    }

    [DepartureSqlFact]
    public async Task DirtyTableCannotOpenAndConcurrentCleaningRejectsStaleVersion()
    {
        await using (var setup = Context())
        {
            await Sessions(setup).Depart(_sessionId, default);
        }

        await using var first = Context();
        await using var second = Context();
        Assert.Equal(409, (await Assert.ThrowsAsync<ManagementException>(() => Sessions(first).OpenForWaiter(_tableId, default))).StatusCode);
        var version = Convert.ToBase64String((await first.Tables.AsNoTracking().SingleAsync(table => table.TableId == _tableId)).Version);
        var codes = await Task.WhenAll(Clean(first, version), Clean(second, version));
        Assert.Equal(new[] { 200, 409 }, codes.Order().ToArray());
        await using var next = Context();
        var opened = await Sessions(next).OpenForWaiter(_tableId, default);
        Assert.NotEqual(_sessionId, opened.TableSessionId);
        await Sessions(next).Depart(_sessionId, default);
        await using var check = Context();
        Assert.Equal("Clean", (await check.Tables.SingleAsync(table => table.TableId == _tableId)).CleaningStatus);
        Assert.Equal(1, await check.TableSessions.CountAsync(session => session.ClosedAt == null));
    }

    [DepartureSqlFact]
    public async Task SqlFailureRollsBackSessionReleasesCleaningAndGuestRevocation()
    {
        await Execute(_connection, "CREATE TRIGGER dbo.FailDeparture ON dbo.GuestAccess AFTER UPDATE AS THROW 51000, 'Test write failure', 1;");
        await using var db = Context();
        await Assert.ThrowsAsync<DbUpdateException>(() => Sessions(db).Depart(_sessionId, default));
        await using var check = Context();
        Assert.Null((await check.TableSessions.SingleAsync()).ClosedAt);
        Assert.All(await check.SessionTables.ToListAsync(), link => Assert.Null(link.ReleasedAt));
        Assert.All(await check.Tables.ToListAsync(), table => Assert.Equal("Clean", table.CleaningStatus));
        Assert.Null((await check.GuestAccesses.SingleAsync()).RevokedAt);
    }

    private TableSessionService Sessions(RestaurantDbContext db)
    {
        return new TableSessionService(null!, new TableRepository(db), new TableSessionRepository(db),
            new SessionTableRepository(db), new ManagementTransaction(new UnitOfWork<RestaurantDbContext>(db)), null!,
            new StaffAccess(), new Scope(_restaurantId), _paid, new GuestAccessRepository(db), Options.Create(Settings()), TimeProvider.System);
    }

    private async Task<int> Clean(RestaurantDbContext db, string version)
    {
        var service = new TableService(new TableRepository(db), new AreaRepository(db),
            new ManagementTransaction(new UnitOfWork<RestaurantDbContext>(db)), null!, new StaffAccess(), new Scope(_restaurantId), Options.Create(Settings()));
        try
        {
            await service.ConfirmClean(_tableId, version, default);
            return 200;
        }
        catch (ManagementException error)
        {
            return error.StatusCode;
        }
    }

    private static ServiceRequestSettings Settings()
    {
        return new ServiceRequestSettings
        {
            WaiterRoleIds = ["waiter"],
            CashierRoleIds = ["cashier"]
        };
    }

    private RestaurantDbContext Context()
    {
        return new RestaurantDbContext(new DbContextOptionsBuilder<RestaurantDbContext>().UseSqlServer(_connection).Options);
    }

    private static async Task Execute(string connection, string text)
    {
        await using var sql = new SqlConnection(connection);
        await sql.OpenAsync();
        await using var command = new SqlCommand(text, sql);
        await command.ExecuteNonQueryAsync();
    }

    public async Task DisposeAsync()
    {
        if (_created)
        {
            Assert.Matches(@"^CodexDeparture_[0-9a-f]{32}$", _database);
            await Execute(_master, $"ALTER DATABASE [{_database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{_database}];");
        }
    }

    private sealed class Scope(Guid restaurantId) : ICurrentRestaurantScopeService
    {
        public Task<CurrentRestaurantScopeDto> GetCurrentScope(Guid? id, CancellationToken ct)
        {
            Assert.True(id is null || id == restaurantId);
            return Task.FromResult(new CurrentRestaurantScopeDto
            {
                UserId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                RoleId = "waiter",
                RestaurantIds = [restaurantId]
            });
        }
    }

    private sealed class PaidClient : ISessionPaymentClient
    {
        public Task RequirePaidAsync(Guid sessionId, Guid restaurantId, CancellationToken ct)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class StaffAccess : ITableSessionOrderAccess
    {
        public Task<Guid> RequireStaffAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(Guid.Parse("11111111-1111-1111-1111-111111111111"));
        }
    }
}
