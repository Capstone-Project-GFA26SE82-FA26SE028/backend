using System.Text.RegularExpressions;
using Identity.BLL.Implements;
using Identity.Common.Settings;
using Identity.DAL.Implements;
using Identity.DAL.UnitOfWorks;
using Identity.Domain.Models;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;

namespace Identity.Tests;

public sealed class IdentityUpgradeSqlFactAttribute : FactAttribute
{
    public IdentityUpgradeSqlFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("IDENTITY_SQL_TEST_CONNECTION")))
        {
            Skip = "Set IDENTITY_SQL_TEST_CONNECTION to run the Identity SQL upgrade tests on an isolated database.";
        }
    }
}

public sealed class StaffTransferUpgradeSqlTests : IAsyncLifetime
{
    private readonly string database = "IdentityUpgradeTest_" + Guid.NewGuid().ToString("N");
    private string master = string.Empty;
    private string connection = string.Empty;
    private bool created;

    public async Task InitializeAsync()
    {
        var configured = Environment.GetEnvironmentVariable("IDENTITY_SQL_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(configured))
        {
            return;
        }

        var builder = new SqlConnectionStringBuilder(configured)
        {
            InitialCatalog = "master",
            Pooling = false
        };
        master = builder.ConnectionString;
        builder.InitialCatalog = database;
        connection = builder.ConnectionString;
        await Execute(master, $"CREATE DATABASE [{database}];");
        created = true;
        var schema = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Sql", "IdentityDb.sql"));
        await RunScript(schema);
    }

    [IdentityUpgradeSqlFact]
    public async Task MissingStaffTransferFailsRoleChangeThenUpgradeRecoversWithoutChangingUsers()
    {
        var actor = Guid.NewGuid();
        var employee = Guid.NewGuid();
        await using (var db = Context())
        {
            db.Roles.AddRange(new Role
            {
                RoleId = "platform_admin",
                RoleName = "Platform Administrator"
            }, new Role
            {
                RoleId = "waiter",
                RoleName = "Waiter"
            }, new Role
            {
                RoleId = "cashier",
                RoleName = "Cashier"
            });
            db.Users.AddRange(User(actor, "platform_admin"), User(employee, "waiter"));
            await db.SaveChangesAsync();
        }

        await Execute(connection, "DROP TABLE dbo.StaffTransfer;");
        await using (var db = Context())
        {
            var error = await Assert.ThrowsAsync<SqlException>(() => Service(db).SetRole(actor, employee, "cashier", default));
            Assert.Equal(208, error.Number);
        }

        await Upgrade();
        await Upgrade();
        await using (var db = Context())
        {
            Assert.Equal(2, await db.Users.CountAsync());
            Assert.Equal("waiter", (await db.Users.SingleAsync(user => user.UserId == employee)).RoleId);
            Assert.Empty(await db.Set<StaffTransfer>().ToListAsync());
            Assert.Equal(200, (await Service(db).SetRole(actor, employee, "cashier", default)).StatusCode);
            Assert.Equal("cashier", await db.Users.Where(user => user.UserId == employee).Select(user => user.RoleId).SingleAsync());
        }
    }

    [IdentityUpgradeSqlFact]
    public async Task ExistingTransferSurvivesRepeatedUpgradeAndStillBlocksRoleChange()
    {
        var actor = Guid.NewGuid();
        var employee = Guid.NewGuid();
        var transfer = new StaffTransfer
        {
            StaffTransferId = Guid.NewGuid(),
            UserId = employee,
            SourceRestaurantId = Guid.NewGuid(),
            TargetRestaurantId = Guid.NewGuid(),
            RequestedBy = actor,
            WasActive = true,
            Status = "Pending",
            Message = "Keep existing transfer",
            CreatedAt = new DateTime(2026, 11, 23, 0, 0, 0, DateTimeKind.Utc)
        };
        await using (var db = Context())
        {
            db.Roles.AddRange(new Role
            {
                RoleId = "platform_admin",
                RoleName = "Administrator"
            }, new Role
            {
                RoleId = "waiter",
                RoleName = "Waiter"
            });
            db.Users.AddRange(User(actor, "platform_admin"), User(employee, "waiter"));
            db.Set<StaffTransfer>().Add(transfer);
            await db.SaveChangesAsync();
        }

        await Execute(connection, "DROP INDEX IX_StaffTransfer_UserId_Status ON dbo.StaffTransfer;");
        await Upgrade();
        await Upgrade();
        await using var verification = Context();
        var saved = Assert.Single(await verification.Set<StaffTransfer>().ToListAsync());
        Assert.Equal(transfer.StaffTransferId, saved.StaffTransferId);
        Assert.Equal(transfer.Message, saved.Message);
        Assert.Equal(transfer.CreatedAt, saved.CreatedAt);
        Assert.Equal(2, await verification.Users.CountAsync());
        Assert.Equal(409, (await Service(verification).SetRole(actor, employee, "waiter", default)).StatusCode);
    }

    [IdentityUpgradeSqlFact]
    public async Task IncompatibleExistingSchemaIsRejectedWithoutRewritingData()
    {
        await Execute(connection, "ALTER TABLE dbo.StaffTransfer ALTER COLUMN Message nvarchar(100) NULL;");
        var error = await Assert.ThrowsAsync<SqlException>(Upgrade);
        Assert.Equal(51000, error.Number);
        await using var db = Context();
        Assert.Empty(await db.Set<StaffTransfer>().ToListAsync());
        await using var sql = new SqlConnection(connection);
        await sql.OpenAsync();
        await using var command = new SqlCommand("SELECT COL_LENGTH('dbo.StaffTransfer', 'Message');", sql);
        Assert.Equal(200, Convert.ToInt32(await command.ExecuteScalarAsync()));
    }

    private static User User(Guid id, string role)
    {
        return new User
        {
            UserId = id,
            UserName = id.ToString("N"),
            Email = id.ToString("N") + "@example.test",
            FullName = "Upgrade test",
            RoleId = role,
            PasswordHash = "not-a-login-fixture",
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
    }

    private static UserService Service(IdentityDbContext db)
    {
        return new UserService(new UnitOfWork(db), Options.Create(new UserManagementSettings
        {
            AdminRoleIds = ["platform_admin"]
        }));
    }

    private IdentityDbContext Context()
    {
        return new IdentityDbContext(new DbContextOptionsBuilder<IdentityDbContext>().UseSqlServer(connection).Options);
    }

    private async Task Upgrade()
    {
        await RunScript(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Sql", "IdentityStaffTransfer.sql")));
    }

    private async Task RunScript(string script)
    {
        foreach (var batch in Regex.Split(script, @"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase))
        {
            if (!string.IsNullOrWhiteSpace(batch))
            {
                await Execute(connection, batch);
            }
        }
    }

    private static async Task Execute(string connectionString, string sql)
    {
        await using var db = new SqlConnection(connectionString);
        await db.OpenAsync();
        await using var command = new SqlCommand(sql, db);
        await command.ExecuteNonQueryAsync();
    }

    public async Task DisposeAsync()
    {
        if (created && database.StartsWith("IdentityUpgradeTest_", StringComparison.Ordinal)
            && Guid.TryParseExact(database["IdentityUpgradeTest_".Length..], "N", out _))
        {
            await Execute(master, $"ALTER DATABASE [{database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{database}];");
        }
    }
}
