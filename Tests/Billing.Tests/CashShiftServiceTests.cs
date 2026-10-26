using System.Globalization;
using Billing.BLL.Implements;
using Billing.BLL.Interfaces;
using Billing.Common.DTOs;
using Billing.Common.Exceptions;
using Billing.Common.Settings;
using Billing.DAL.Implements;
using Billing.DAL.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Shared.Persistence.UnitOfWorks;

namespace Billing.Tests;

public sealed class CashShiftServiceTests
{
    [Theory]
    [InlineData("0")]
    [InlineData("250000.50")]
    [InlineData("9999999999999999.99")]
    public async Task CashierOpensOwnBranchWithExplicitCashAndServerIdentity(string amount)
    {
        await using var fixture = await ShiftFixture.Create();
        var result = await fixture.Service.Open(fixture.RegisterId, new OpenCashShiftDTO
        {
            OpeningCash = decimal.Parse(amount, CultureInfo.InvariantCulture)
        }, default);

        fixture.Db.ChangeTracker.Clear();
        var saved = await fixture.Db.CashShifts.SingleAsync();
        Assert.Equal(result.CashShiftId, saved.CashShiftId);
        Assert.Equal(fixture.Identity.Scope.UserId, saved.CashierUserRef);
        Assert.Equal(result.OpeningCash, saved.OpeningCash);
        Assert.Equal("Active", saved.Status);
        Assert.Null(saved.ClosedAt);
        Assert.Equal(ShiftFixture.Now, result.OpenedAt);
        Assert.Equal(fixture.RestaurantId, result.RestaurantId);
    }

    [Theory]
    [InlineData("admin", true)]
    [InlineData("manager", false)]
    [InlineData("waiter", false)]
    [InlineData("kitchen", false)]
    [InlineData("inventory", false)]
    public async Task OnlyCashierRoleMayOpen(string role, bool administrator)
    {
        await using var fixture = await ShiftFixture.Create();
        fixture.Identity.Scope.RoleId = role;
        fixture.Identity.Scope.IsAdministrator = administrator;
        await Expect(403, () => fixture.Service.Open(fixture.RegisterId,
            Request(0), default));
        Assert.Empty(await fixture.Db.CashShifts.ToListAsync());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("-0.01")]
    [InlineData("0.001")]
    [InlineData("10000000000000000")]
    public async Task InvalidOrMissingOpeningCashDoesNotCreateAShift(string? amount)
    {
        await using var fixture = await ShiftFixture.Create();
        await Expect(400, () => fixture.Service.Open(fixture.RegisterId, new OpenCashShiftDTO
        {
            OpeningCash = amount is null ? null : decimal.Parse(amount, CultureInfo.InvariantCulture)
        }, default));
        Assert.Empty(await fixture.Db.CashShifts.ToListAsync());
    }

    [Fact]
    public async Task MissingSessionAndMissingAssignmentsAreRejected()
    {
        await using var fixture = await ShiftFixture.Create();
        fixture.Identity.Scope.UserId = Guid.Empty;
        await Expect(401, () => fixture.Service.Open(fixture.RegisterId,
            Request(0), default));
        fixture.Identity.Scope.UserId = Guid.NewGuid();
        fixture.Identity.Scope.RestaurantIds = [];
        await Expect(403, () => fixture.Service.Open(fixture.RegisterId,
            Request(0), default));
        Assert.Empty(await fixture.Db.CashShifts.ToListAsync());
    }

    [Fact]
    public async Task WrongBranchAndRevocationDuringLockWaitAreRejected()
    {
        await using var fixture = await ShiftFixture.Create();
        fixture.Identity.TargetScope = new BillingAccessScope
        {
            UserId = fixture.Identity.Scope.UserId,
            RoleId = "cashier",
            RestaurantIds = [Guid.NewGuid()]
        };
        await Expect(403, () => fixture.Service.Open(fixture.RegisterId,
            Request(0), default));
        fixture.Identity.TargetScope.RestaurantIds = [];
        await Expect(403, () => fixture.Service.Open(fixture.RegisterId,
            Request(0), default));
        Assert.Empty(await fixture.Db.CashShifts.ToListAsync());
    }

    [Fact]
    public async Task UnknownRegisterAndEmptyIdAreRejected()
    {
        await using var fixture = await ShiftFixture.Create();
        await Expect(404, () => fixture.Service.Open(Guid.NewGuid(),
            Request(0), default));
        await Expect(400, () => fixture.Service.Open(Guid.Empty,
            Request(0), default));
    }

    [Fact]
    public async Task RepeatedOpeningDoesNotReplaceShiftOrOpeningCash()
    {
        await using var fixture = await ShiftFixture.Create();
        var first = await fixture.Service.Open(fixture.RegisterId, Request(100), default);
        await Expect(409, () => fixture.Service.Open(fixture.RegisterId,
            Request(999), default));
        fixture.Db.ChangeTracker.Clear();
        var saved = await fixture.Db.CashShifts.SingleAsync();
        Assert.Equal(first.CashShiftId, saved.CashShiftId);
        Assert.Equal(100, saved.OpeningCash);
    }

    [Fact]
    public async Task UnclosedHistoricalRecordIsNotSilentlyReplaced()
    {
        await using var fixture = await ShiftFixture.Create();
        fixture.Db.CashShifts.Add(new CashShift
        {
            CashShiftId = Guid.NewGuid(),
            CashRegisterId = fixture.RegisterId,
            CashierUserRef = Guid.NewGuid(),
            OpeningCash = 0,
            OpenedAt = ShiftFixture.Now.UtcDateTime.AddHours(-1),
            Status = "Closed"
        });
        await fixture.Db.SaveChangesAsync();
        await Expect(409, () => fixture.Service.Open(fixture.RegisterId,
            Request(0), default));
        Assert.Single(await fixture.Db.CashShifts.ToListAsync());
    }

    [Fact]
    public async Task FailedWriteRollsBackAndDoesNotLeaveAnOpenShift()
    {
        await using var fixture = await ShiftFixture.Create();
        await fixture.Db.Database.ExecuteSqlRawAsync(
            "CREATE TRIGGER FailShift BEFORE INSERT ON CashShift BEGIN SELECT RAISE(ABORT, 'Test write failure'); END;");
        await Assert.ThrowsAsync<DbUpdateException>(() => fixture.Service.Open(fixture.RegisterId,
            Request(100), default));
        fixture.Db.ChangeTracker.Clear();
        Assert.Empty(await fixture.Db.CashShifts.ToListAsync());
    }

    [Fact]
    public async Task RegisterLockRequiresAnActiveTransaction()
    {
        await using var fixture = await ShiftFixture.Create();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new CashRegisterRepository(fixture.Db).LockByIdAsync(fixture.RegisterId, default));
    }

    private static OpenCashShiftDTO Request(decimal amount)
    {
        return new OpenCashShiftDTO
        {
            OpeningCash = amount
        };
    }

    [Theory]
    [InlineData("admin", true)]
    [InlineData("manager", false)]
    [InlineData("cashier", false)]
    public async Task CurrentShiftUsesExistingRegisterReadScope(string role, bool administrator)
    {
        await using var fixture = await ShiftFixture.Create();
        var opened = await fixture.Service.Open(fixture.RegisterId, Request(100), default);
        fixture.Identity.Scope.RoleId = role;
        fixture.Identity.Scope.IsAdministrator = administrator;
        fixture.Identity.Scope.UserId = Guid.NewGuid();
        if (administrator)
        {
            fixture.Identity.Scope.RestaurantIds = [];
        }

        var current = await fixture.Service.GetCurrent(fixture.RegisterId, default);
        Assert.Equal(opened.CashShiftId, current.CashShiftId);
        Assert.Equal(opened.CashierUserRef, current.CashierUserRef);
        Assert.Equal(100, current.OpeningCash);
    }

    [Theory]
    [InlineData("waiter")]
    [InlineData("kitchen")]
    [InlineData("inventory")]
    public async Task OtherRolesCannotReadAnOpenShift(string role)
    {
        await using var fixture = await ShiftFixture.Create();
        await fixture.Service.Open(fixture.RegisterId, Request(0), default);
        fixture.Identity.Scope.RoleId = role;
        await Expect(403, () => fixture.Service.GetCurrent(fixture.RegisterId, default));
    }

    [Fact]
    public async Task CurrentShiftChecksBranchAndRevokedAssignments()
    {
        await using var fixture = await ShiftFixture.Create();
        await fixture.Service.Open(fixture.RegisterId, Request(0), default);
        fixture.Identity.Scope.RestaurantIds = [Guid.NewGuid()];
        await Expect(403, () => fixture.Service.GetCurrent(fixture.RegisterId, default));
        fixture.Identity.Scope.RestaurantIds = [];
        await Expect(403, () => fixture.Service.GetCurrent(fixture.RegisterId, default));
    }

    [Fact]
    public async Task NoOpenShiftReturnsNotFoundAndDoesNotOpenOne()
    {
        await using var fixture = await ShiftFixture.Create();
        await Expect(404, () => fixture.Service.GetCurrent(fixture.RegisterId, default));
        fixture.Db.CashShifts.Add(new CashShift
        {
            CashShiftId = Guid.NewGuid(),
            CashRegisterId = fixture.RegisterId,
            CashierUserRef = fixture.Identity.Scope.UserId,
            OpeningCash = 0,
            OpenedAt = ShiftFixture.Now.UtcDateTime.AddHours(-2),
            ClosedAt = ShiftFixture.Now.UtcDateTime.AddHours(-1),
            Status = "Closed"
        });
        await fixture.Db.SaveChangesAsync();
        await Expect(404, () => fixture.Service.GetCurrent(fixture.RegisterId, default));
        Assert.Single(await fixture.Db.CashShifts.ToListAsync());
    }

    [Fact]
    public async Task InconsistentUnclosedRecordsAreReportedInsteadOfChoosingOrChangingOne()
    {
        await using var fixture = await ShiftFixture.Create();
        await fixture.Service.Open(fixture.RegisterId, Request(0), default);
        fixture.Db.CashShifts.Add(new CashShift
        {
            CashShiftId = Guid.NewGuid(),
            CashRegisterId = fixture.RegisterId,
            CashierUserRef = Guid.NewGuid(),
            OpeningCash = 10,
            OpenedAt = ShiftFixture.Now.UtcDateTime,
            Status = "Active"
        });
        await fixture.Db.SaveChangesAsync();
        await Expect(409, () => fixture.Service.GetCurrent(fixture.RegisterId, default));
        Assert.Equal(2, await fixture.Db.CashShifts.CountAsync());
    }

    private static async Task Expect(int status, Func<Task> action)
    {
        var error = await Assert.ThrowsAsync<BillingException>(action);
        Assert.Equal(status, error.StatusCode);
    }
}

internal sealed class ShiftFixture : IAsyncDisposable
{
    public static DateTimeOffset Now { get; } = new(2026, 10, 26, 4, 0, 0, TimeSpan.Zero);
    public Guid RestaurantId { get; } = Guid.NewGuid();
    public Guid RegisterId { get; } = Guid.NewGuid();
    public BillingDbContext Db { get; }
    public ShiftIdentity Identity { get; }
    public CashShiftService Service { get; }
    private readonly SqliteConnection _connection;

    private ShiftFixture(SqliteConnection connection)
    {
        _connection = connection;
        Db = new BillingDbContext(new DbContextOptionsBuilder<BillingDbContext>().UseSqlite(connection).Options);
        Identity = new ShiftIdentity
        {
            Scope = new BillingAccessScope
            {
                UserId = Guid.NewGuid(),
                RoleId = "cashier",
                RestaurantIds = [RestaurantId]
            }
        };
        Service = new CashShiftService(new CashRegisterRepository(Db), new CashShiftRepository(Db),
            new UnitOfWork<BillingDbContext>(Db), Identity, Options.Create(new CashRegisterAccessSettings
            {
                ManagerRoleIds = ["manager"],
                CashierRoleIds = ["cashier"]
            }), new ShiftClock(), new CashRegisterService(new CashRegisterRepository(Db), new UnitOfWork<BillingDbContext>(Db),
                Identity, Options.Create(new CashRegisterAccessSettings
                {
                    ManagerRoleIds = ["manager"],
                    CashierRoleIds = ["cashier"]
                })));
    }

    public static async Task<ShiftFixture> Create()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var fixture = new ShiftFixture(connection);
        await fixture.Db.Database.ExecuteSqlRawAsync(
            "CREATE TABLE CashRegister (CashRegisterId TEXT NOT NULL PRIMARY KEY, RestaurantId TEXT NOT NULL, Code TEXT NOT NULL);");
        await fixture.Db.Database.ExecuteSqlRawAsync(
            "CREATE TABLE CashShift (CashShiftId TEXT NOT NULL PRIMARY KEY, CashRegisterId TEXT NOT NULL REFERENCES CashRegister(CashRegisterId), CashierUserRef TEXT NOT NULL, OpeningCash TEXT NOT NULL, OpenedAt TEXT NOT NULL, ClosedAt TEXT NULL, Status TEXT NOT NULL);");
        fixture.Db.CashRegisters.Add(new CashRegister
        {
            CashRegisterId = fixture.RegisterId,
            RestaurantId = fixture.RestaurantId,
            Code = "POS-TEST"
        });
        await fixture.Db.SaveChangesAsync();
        return fixture;
    }

    public async ValueTask DisposeAsync()
    {
        await Db.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private sealed class ShiftClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow()
        {
            return Now;
        }
    }
}

internal sealed class ShiftIdentity : IBillingAccess
{
    public BillingAccessScope Scope { get; set; } = new();
    public BillingAccessScope? TargetScope { get; set; }

    public Task<BillingAccessScope> GetCurrentScopeAsync(Guid? restaurantId, CancellationToken cancellationToken)
    {
        return Task.FromResult(restaurantId.HasValue ? TargetScope ?? Scope : Scope);
    }
}
