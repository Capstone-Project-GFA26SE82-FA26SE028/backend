using System.Text.RegularExpressions;
using Billing.BLL.Implements;
using Billing.Common.DTOs;
using Billing.Common.Exceptions;
using Billing.Common.Settings;
using Billing.DAL.Implements;
using Billing.DAL.Models;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Shared.Persistence.UnitOfWorks;

namespace Billing.Tests;

public sealed class BillSqlServerTests : IAsyncLifetime
{
    private const string Prefix = "CodexBillingBill_";
    private readonly string _database = Prefix + Guid.NewGuid().ToString("N");
    private string _master = string.Empty;
    private string _connection = string.Empty;
    private bool _created;

    public async Task InitializeAsync()
    {
        var configured = Environment.GetEnvironmentVariable("BILLING_SQL_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(configured))
        {
            return;
        }

        RequireOwnedDatabase();
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
        var schema = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Sql", "BillingDb.sql"));
        foreach (var batch in Regex.Split(schema, @"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase))
        {
            if (!string.IsNullOrWhiteSpace(batch))
            {
                await Execute(_connection, batch);
            }
        }

        await using var db = Context();
        db.TaxFeePolicies.Add(new TaxFeePolicy
        {
            TaxFeePolicyId = Guid.NewGuid(),
            Version = 1,
            VatRate = 0.1m,
            ServiceRate = 0.05m,
            ServiceEnabled = true,
            EffectiveFrom = DateTime.UtcNow.AddDays(-1)
        });
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        if (_created)
        {
            RequireOwnedDatabase();
            await Execute(_master, $"ALTER DATABASE [{_database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{_database}];");
        }
    }

    [BillingSqlServerFact]
    public async Task ConcurrentBillsForOneSessionCreateExactlyOneSnapshot()
    {
        var source = new BillServiceTests.BillSource();
        await using var first = Context();
        await using var second = Context();
        var results = await Task.WhenAll(Create(first, source), Create(second, source));
        Assert.Equal(new[] { 201, 409 }, results.Order().ToArray());
        await using var check = Context();
        var bill = await check.Bills.Include(value => value.BillLines).SingleAsync();
        Assert.Equal(source.Value.RestaurantId, bill.RestaurantId);
        Assert.Null(Assert.Single(bill.BillLines).CogsSnapshot);
        Assert.Equal(232m, bill.TotalAmount);
        Assert.Equal(8, bill.Version.Length);
        Assert.Empty(await check.Payments.ToListAsync());
    }

    [BillingSqlServerFact]
    public async Task UpgradeIsRepeatableAndDoesNotChangeKeysOrCreateFakeDefaults()
    {
        var upgrade = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Sql", "BillingBillRestaurant.sql"));
        await Execute(_connection, "ALTER TABLE dbo.Bill DROP COLUMN RestaurantId; ALTER TABLE dbo.BillLine ALTER COLUMN CogsSnapshot decimal(18,2) NOT NULL;");
        await Execute(_connection, upgrade);
        await Execute(_connection, upgrade);
        await using var connection = new SqlConnection(_connection);
        await connection.OpenAsync();
        await using var query = new SqlCommand("SELECT COUNT(*) FROM sys.columns WHERE (object_id=OBJECT_ID('dbo.Bill') AND name='RestaurantId' AND is_nullable=0 AND default_object_id=0) OR (object_id=OBJECT_ID('dbo.BillLine') AND name='CogsSnapshot' AND is_nullable=1 AND precision=18 AND scale=2 AND default_object_id=0);", connection);
        Assert.Equal(2, (int)(await query.ExecuteScalarAsync())!);
    }

    [BillingSqlServerFact]
    public async Task LegacyBillsWithoutRestaurantMappingsRejectUpgradeWithoutChangingCogs()
    {
        await Execute(_connection, "ALTER TABLE dbo.Bill DROP COLUMN RestaurantId; ALTER TABLE dbo.BillLine ALTER COLUMN CogsSnapshot decimal(18,2) NOT NULL; INSERT dbo.Bill (BillId,BillNumber,Status,SubTotal,DiscountAmount,VatRateSnapshot,ServiceRateSnapshot,VatAmount,ServiceAmount,TotalAmount,Currency) VALUES (NEWID(),'LEGACY','Unpaid',1,0,0,0,0,0,1,'VND');");
        var upgrade = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Sql", "BillingBillRestaurant.sql"));
        await Assert.ThrowsAsync<SqlException>(() => Execute(_connection, upgrade));
        await using var connection = new SqlConnection(_connection);
        await connection.OpenAsync();
        await using var query = new SqlCommand("SELECT COUNT(*) FROM dbo.Bill; SELECT is_nullable FROM sys.columns WHERE object_id=OBJECT_ID('dbo.BillLine') AND name='CogsSnapshot';", connection);
        await using var reader = await query.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(1, reader.GetInt32(0));
        await reader.NextResultAsync();
        Assert.True(await reader.ReadAsync());
        Assert.False(reader.GetBoolean(0));
    }

    private static async Task<int> Create(BillingDbContext db, BillServiceTests.BillSource source)
    {
        try
        {
            await Service(db, source).Create(source.Value.TableSessionId, default);
            return 201;
        }
        catch (BillingException error)
        {
            return error.StatusCode;
        }
    }

    [BillingSqlServerFact]
    public async Task ConcurrentRefreshRequestsSerializeAndDoNotDuplicateItems()
    {
        var source = new BillServiceTests.BillSource();
        await using var setup = Context();
        var bill = await Service(setup, source).Create(source.Value.TableSessionId, default);
        var originalId = bill.Items[0].BillLineId;
        source.Value.Items[0].Quantity = 2;
        source.Value.Items.Add(new BillSourceLineDTO
        {
            OrderItemId = Guid.NewGuid(),
            OriginalTableId = source.Value.Items[0].OriginalTableId,
            Quantity = 1,
            Item = new BillItemSnapshot
            {
                OrderId = Guid.NewGuid(),
                ItemId = Guid.NewGuid(),
                Name = "SQL snapshot drink",
                BaseUnitPrice = 10,
                Status = "Served"
            }
        });
        await using var first = Context();
        await using var second = Context();
        var results = await Task.WhenAll(Service(first, source).Refresh(bill.BillId, default),
            Service(second, source).Refresh(bill.BillId, default));
        Assert.All(results, value => Assert.Equal(476m, value.TotalAmount));
        await using var check = Context();
        var stored = await check.Bills.Include(value => value.BillLines).SingleAsync();
        Assert.Equal(412.50m, stored.SubTotal);
        Assert.Equal(2, stored.BillLines.Count);
        Assert.Contains(stored.BillLines, line => line.BillLineId == originalId);
        Assert.NotEqual(bill.Version, Convert.ToBase64String(stored.Version));
        Assert.Empty(await check.Payments.ToListAsync());
    }

    [BillingSqlServerFact]
    public async Task RefreshWaitingOnBillLockRechecksPaidStateAfterCommit()
    {
        var source = new BillServiceTests.BillSource();
        await using var setup = Context();
        var bill = await Service(setup, source).Create(source.Value.TableSessionId, default);
        await using var writer = Context();
        await using var transaction = await writer.Database.BeginTransactionAsync();
        await writer.Database.ExecuteSqlInterpolatedAsync($"UPDATE dbo.Bill SET Status = 'Paid', PaidAt = GETUTCDATE() WHERE BillId = {bill.BillId}");
        await using var refresher = Context();
        var refresh = Service(refresher, source).Refresh(bill.BillId, default);
        await Task.Delay(150);
        Assert.False(refresh.IsCompleted);
        await transaction.CommitAsync();
        var error = await Assert.ThrowsAsync<BillingException>(() => refresh);
        Assert.Equal(409, error.StatusCode);
        await using var check = Context();
        var stored = await check.Bills.AsNoTracking().SingleAsync();
        Assert.Equal("Paid", stored.Status);
        Assert.Equal(bill.TotalAmount, stored.TotalAmount);
    }

    private static BillService Service(BillingDbContext db, BillServiceTests.BillSource source)
    {
        var identity = new ShiftIdentity
        {
            Scope = new BillingAccessScope
            {
                UserId = Guid.NewGuid(),
                RoleId = "cashier",
                RestaurantIds = [source.Value.RestaurantId]
            }
        };
        return new BillService(new BillRepository(db), new TaxFeePolicyRepository(db),
            new UnitOfWork<BillingDbContext>(db), identity, source, Options.Create(new CashRegisterAccessSettings
            {
                CashierRoleIds = ["cashier"],
                ManagerRoleIds = ["manager"]
            }), Options.Create(new BillSettings
            {
                Currency = "VND",
                OrderPriceBasis = "Exclusive"
            }), TimeProvider.System);
    }

    [BillingSqlServerFact]
    public async Task CheckoutUpgradePreservesRowsAndFilteredIndexAllowsOnlyOneUnreleasedAttempt()
    {
        var source = new BillServiceTests.BillSource();
        await using var db = Context();
        var bill = await Service(db, source).Create(source.Value.TableSessionId, default);
        var first = Guid.NewGuid();
        await Execute(_connection, $"INSERT dbo.BillCheckout (CheckoutId,BillId,TableSessionRef,Status,CreatedAt) VALUES ('{first}','{bill.BillId}','{source.Value.TableSessionId}','Preparing',SYSUTCDATETIME());");
        var upgrade = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Sql", "BillingCheckout.sql"));
        upgrade = Regex.Replace(upgrade, @"^\s*GO\s*$", string.Empty, RegexOptions.Multiline | RegexOptions.IgnoreCase);
        await Execute(_connection, upgrade);
        await Execute(_connection, upgrade);
        var error = await Assert.ThrowsAsync<SqlException>(() => Execute(_connection,
            $"INSERT dbo.BillCheckout (CheckoutId,TableSessionRef,Status,CreatedAt) VALUES (NEWID(),'{source.Value.TableSessionId}','Preparing',SYSUTCDATETIME());"));
        Assert.Contains(error.Number, new[] { 2601, 2627 });
        await Execute(_connection, $"UPDATE dbo.BillCheckout SET Status='Released',ReleasedAt=SYSUTCDATETIME() WHERE CheckoutId='{first}'; INSERT dbo.BillCheckout (CheckoutId,TableSessionRef,Status,CreatedAt) VALUES (NEWID(),'{source.Value.TableSessionId}','Preparing',SYSUTCDATETIME());");
        Assert.Equal(2, await db.BillCheckouts.CountAsync());
        Assert.Equal(1, await db.BillCheckouts.CountAsync(value => value.ReleasedAt == null));
        Assert.All(await db.BillCheckouts.ToListAsync(), value => Assert.Null(value.SourceHash));
        Assert.Single(await db.Bills.ToListAsync());
        Assert.Equal(201.25m, (await db.Bills.SingleAsync()).SubTotal);
    }

    private BillingDbContext Context()
    {
        return new BillingDbContext(new DbContextOptionsBuilder<BillingDbContext>().UseSqlServer(_connection).Options);
    }

    private void RequireOwnedDatabase()
    {
        if (!_database.StartsWith(Prefix, StringComparison.Ordinal)
            || !Guid.TryParseExact(_database[Prefix.Length..], "N", out _))
        {
            throw new InvalidOperationException("Only this test's isolated Billing database may be created or removed.");
        }
    }

    private static async Task Execute(string connectionString, string sql)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}
