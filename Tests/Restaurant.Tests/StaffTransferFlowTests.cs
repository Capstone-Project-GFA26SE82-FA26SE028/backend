using Identity.BLL.Implements;
using Identity.BLL.Interfaces;
using Identity.Common.DTOs;
using Identity.Common.Settings;
using Identity.DAL.Implements;
using Identity.Domain.Models;
using Microsoft.Data.Sqlite;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Restaurant.BLL.Implements;
using Restaurant.BLL.Interfaces;
using Restaurant.Common.DTOs;
using Restaurant.DAL.Implements;
using Restaurant.DAL.Models;
using Xunit;

namespace Restaurant.Tests;

public sealed class StaffTransferFlowTests : IAsyncLifetime
{
    private readonly SqliteConnection identityConnection = new("Data Source=:memory:");
    private readonly SqliteConnection restaurantConnection = new("Data Source=:memory:");
    private IdentityDbContext identity = null!;
    private RestaurantDbContext restaurant = null!;
    private StaffTransferService service = null!;
    private string? identitySql;
    private string? restaurantSql;
    private TransferRemote remote = null!;
    private readonly Guard guard = new();
    private readonly Guid platform = Guid.NewGuid();
    private readonly Guid admin = Guid.NewGuid();
    private readonly Guid employee = Guid.NewGuid();
    private readonly Guid source = Guid.NewGuid();
    private readonly Guid target = Guid.NewGuid();

    public async Task InitializeAsync()
    {
        await identityConnection.OpenAsync();
        await restaurantConnection.OpenAsync();
        var server = Environment.GetEnvironmentVariable("RESTAURANT_SQL_TEST_SERVER");
        var identityOptions = new DbContextOptionsBuilder<IdentityDbContext>();
        var restaurantOptions = new DbContextOptionsBuilder<RestaurantDbContext>();
        if (string.IsNullOrWhiteSpace(server))
        {
            identityOptions.UseSqlite(identityConnection);
            restaurantOptions.UseSqlite(restaurantConnection);
        }
        else
        {
            identitySql = SqlConnection(server, "CodexStaffTransferIdentity_");
            restaurantSql = SqlConnection(server, "CodexStaffTransferRestaurant_");
            identityOptions.UseSqlServer(identitySql);
            restaurantOptions.UseSqlServer(restaurantSql);
        }

        identity = new IdentityDbContext(identityOptions.Options);
        restaurant = new RestaurantDbContext(restaurantOptions.Options);
        await identity.Database.EnsureCreatedAsync();
        await restaurant.Database.EnsureCreatedAsync();
        foreach (var pair in new[] { (platform, "platform_admin"), (admin, "admin"), (employee, "waiter") })
        {
            identity.Roles.Add(new Role
            {
                RoleId = pair.Item2,
                RoleName = pair.Item2
            });
            identity.Users.Add(new User
            {
                UserId = pair.Item1,
                RoleId = pair.Item2,
                RestaurantId = pair.Item1 == platform ? null : source,
                UserName = pair.Item2,
                FullName = pair.Item2,
                Email = pair.Item2 + "@test.local",
                PasswordHash = "test-only",
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            });
        }

        identity.UserSessions.Add(new UserSession
        {
            UserSessionId = Guid.NewGuid(),
            UserId = employee,
            LoggedInAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddHours(1),
            RefreshTokenHash = new string('a', 64)
        });
        foreach (var id in new[] { source, target })
        {
            restaurant.Restaurants.Add(new Restaurant.DAL.Models.Restaurant
            {
                RestaurantId = id,
                RestaurantName = id.ToString(),
                Address = "Test",
                TimeZone = "UTC"
            });
        }

        restaurant.RestaurantStaffs.Add(new RestaurantStaff
        {
            RestaurantId = source,
            UserIdRef = employee,
            IsActive = true
        });
        await identity.SaveChangesAsync();
        await restaurant.SaveChangesAsync();
        identity.ChangeTracker.Clear();
        restaurant.ChangeTracker.Clear();
        var receipt = new StaffTransferReceiptService(new StaffTransferReceiptRepository(restaurant),
            new RestaurantStaffRepository(restaurant), new RestaurantRepository(restaurant),
            new ServiceRequestRepository(restaurant), new ManagementTransaction(
                new Shared.Persistence.UnitOfWorks.UnitOfWork<RestaurantDbContext>(restaurant)), guard);
        remote = new TransferRemote(receipt);
        service = new StaffTransferService(new StaffTransferRepository(identity), new UserRepository(identity),
            new SessionRepository(identity), new Identity.DAL.UnitOfWorks.UnitOfWork(identity), remote,
            new Names(target), Options.Create(new UserManagementSettings
            {
                AdminRoleIds = ["platform_admin"]
            }));
    }

    [Fact]
    public async Task TransferRecoversLostReplyWithoutDuplicateAssignmentsAndRevokesSessions()
    {
        var id = Guid.NewGuid();
        Assert.Equal(403, (await service.Create(admin, id, employee, target, default)).StatusCode);
        Assert.Equal(202, (await service.Create(platform, id, employee, target, default)).StatusCode);
        Assert.False((await identity.Users.FindAsync(employee))!.IsActive);
        Assert.NotNull((await identity.UserSessions.SingleAsync()).RevokedAt);
        Assert.Equal(202, (await service.Create(platform, id, employee, target, default)).StatusCode);
        Assert.Equal(409, (await service.Create(platform, Guid.NewGuid(), employee, target, default)).StatusCode);
        var accounts = new UserService(new Identity.DAL.UnitOfWorks.UnitOfWork(identity),
            Options.Create(new UserManagementSettings
            {
                AdminRoleIds = ["platform_admin"]
            }));
        Assert.Equal(409, (await accounts.SetStatus(platform, employee, true, default)).StatusCode);
        remote.LoseReply = true;
        await service.Process(id, default);
        Assert.Equal(source, (await identity.Users.FindAsync(employee))!.RestaurantId);
        Assert.False((await identity.Users.FindAsync(employee))!.IsActive);
        Assert.True((await restaurant.RestaurantStaffs.SingleAsync(item => item.RestaurantId == target)).IsActive);
        remote.LoseReply = false;
        await service.Process(id, default);
        await service.Process(id, default);
        var employeeUser = await identity.Users.FindAsync(employee);
        Assert.Equal(target, employeeUser!.RestaurantId);
        Assert.True(employeeUser.IsActive);
        Assert.NotNull((await identity.UserSessions.SingleAsync()).RevokedAt);
        Assert.False((await restaurant.RestaurantStaffs.SingleAsync(item => item.RestaurantId == source)).IsActive);
        Assert.Single(await restaurant.Set<StaffTransferReceipt>().ToListAsync());
        Assert.Equal("Completed", Assert.IsType<StaffTransferDTO>((await service.GetById(platform, id, default)).Result).Status);
        Assert.Equal(403, (await service.GetById(admin, id, default)).StatusCode);
        Assert.Contains(await identity.AuditLogs.ToListAsync(), item => item.Action == "STAFF_TRANSFER_COMPLETED");
    }

    [Fact]
    public async Task KitchenResponsibilityRejectsTransferAndRestoresOriginalAccount()
    {
        guard.Assigned = true;
        var id = Guid.NewGuid();
        await service.Create(platform, id, employee, target, default);
        await service.Process(id, default);
        var result = await service.GetById(platform, id, default);
        Assert.Equal(409, result.StatusCode);
        Assert.Equal("Rejected", Assert.IsType<StaffTransferDTO>(result.Result).Status);
        Assert.True((await identity.Users.FindAsync(employee))!.IsActive);
        Assert.Equal(source, (await identity.Users.FindAsync(employee))!.RestaurantId);
        Assert.True((await restaurant.RestaurantStaffs.SingleAsync()).IsActive);
        Assert.NotNull((await identity.UserSessions.SingleAsync()).RevokedAt);
    }

    [Fact]
    public async Task UnfinishedServiceRequestBlocksTransferWithoutChangingAssignments()
    {
        var sessionId = Guid.NewGuid();
        restaurant.ServiceRequests.Add(new ServiceRequest
        {
            ServiceRequestId = Guid.NewGuid(),
            Type = "Payment",
            Status = "Acknowledged",
            HandledByRef = employee,
            RequestedAt = DateTime.UtcNow,
            AcknowledgedAt = DateTime.UtcNow,
            TableSession = new TableSession
            {
                TableSessionId = sessionId,
                Status = "Open",
                PartySize = 1,
                OpenedAt = DateTime.UtcNow
            }
        });
        await restaurant.SaveChangesAsync();
        var id = Guid.NewGuid();
        await service.Create(platform, id, employee, target, default);
        await service.Process(id, default);
        var result = await service.GetById(platform, id, default);
        Assert.Equal(409, result.StatusCode);
        Assert.Equal(source, (await identity.Users.FindAsync(employee))!.RestaurantId);
        Assert.True((await restaurant.RestaurantStaffs.SingleAsync()).IsActive);
        Assert.Equal("Acknowledged", (await restaurant.ServiceRequests.SingleAsync()).Status);
    }

    [Fact]
    public async Task DoesNotTransferAdminOrUnknownDestination()
    {
        Assert.Equal(409, (await service.Create(platform, Guid.NewGuid(), admin, target, default)).StatusCode);
        Assert.Equal(404, (await service.Create(platform, Guid.NewGuid(), employee, Guid.NewGuid(), default)).StatusCode);
        Assert.Empty(await identity.Set<StaffTransfer>().ToListAsync());
        Assert.Null((await identity.UserSessions.SingleAsync()).RevokedAt);
    }

    private sealed class Names(Guid target) : IRestaurantNames
    {
        public Task<IReadOnlyDictionary<Guid, string>> GetAsync(Guid[] ids, CancellationToken ct)
        {
            return Task.FromResult<IReadOnlyDictionary<Guid, string>>(ids.Contains(target)
                ? new Dictionary<Guid, string> { [target] = "Target" } : new Dictionary<Guid, string>());
        }
    }

    private sealed class Guard : IStaffTransferGuard
    {
        public bool Assigned { get; set; }

        public Task<bool> HasKitchenAssignmentAsync(Guid userId, CancellationToken ct)
        {
            return Task.FromResult(Assigned);
        }
    }

    private sealed class TransferRemote(StaffTransferReceiptService receipt) : IStaffTransferRemote
    {
        public bool IsConfigured => true;
        public bool LoseReply { get; set; }

        public async Task<StaffTransferResult> ApplyAsync(Guid id, Guid userId, Guid source, Guid target, CancellationToken ct)
        {
            var result = await receipt.Apply(new StaffAssignmentTransferDto(id, userId, source, target), ct);
            return LoseReply ? new("Pending", null) : new(result.Status, result.Message);
        }
    }

    [SqlServerFact]
    public async Task ConcurrentTransfersForOneEmployeeHaveOnlyOnePendingOperation()
    {
        async Task<int> Start()
        {
            await using var db = new IdentityDbContext(new DbContextOptionsBuilder<IdentityDbContext>().UseSqlServer(identitySql).Options);
            var service = new StaffTransferService(new StaffTransferRepository(db), new UserRepository(db),
                new SessionRepository(db), new Identity.DAL.UnitOfWorks.UnitOfWork(db), remote, new Names(target),
                Options.Create(new UserManagementSettings
            {
                AdminRoleIds = ["platform_admin"]
            }));
            return (await service.Create(platform, Guid.NewGuid(), employee, target, default)).StatusCode;
        }

        var results = await Task.WhenAll(Start(), Start());
        Assert.Single(results, status => status == 202);
        Assert.Single(results, status => status == 409);
        Assert.Single(await identity.Set<StaffTransfer>().AsNoTracking().ToListAsync());
    }

    private static string SqlConnection(string server, string prefix)
    {
        return new SqlConnectionStringBuilder
        {
            DataSource = server,
            InitialCatalog = prefix + Guid.NewGuid().ToString("N"),
            IntegratedSecurity = true,
            TrustServerCertificate = true
        }.ConnectionString;
    }

    private static void CheckTestDatabase(string connection, string prefix)
    {
        var name = new SqlConnectionStringBuilder(connection).InitialCatalog;
        Assert.StartsWith(prefix, name);
        Assert.True(Guid.TryParseExact(name[prefix.Length..], "N", out _));
    }

    public async Task DisposeAsync()
    {
        if (identitySql is not null && identity is not null)
        {
            CheckTestDatabase(identitySql, "CodexStaffTransferIdentity_");
            await identity.Database.EnsureDeletedAsync();
        }

        if (restaurantSql is not null && restaurant is not null)
        {
            CheckTestDatabase(restaurantSql, "CodexStaffTransferRestaurant_");
            await restaurant.Database.EnsureDeletedAsync();
        }

        if (identity is not null)
        {
            await identity.DisposeAsync();
        }

        if (restaurant is not null)
        {
            await restaurant.DisposeAsync();
        }
        await identityConnection.DisposeAsync();
        await restaurantConnection.DisposeAsync();
    }
}
