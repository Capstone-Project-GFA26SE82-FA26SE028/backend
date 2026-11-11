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

public sealed class CashPaymentSqlServerTests : IAsyncLifetime
{
    private const string Prefix = "CodexBillingCash_";
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
    public async Task ConcurrentCashRequestsWithDifferentKeysCollectExactlyOnce()
    {
        var source = new BillServiceTests.BillSource();
        var actor = CashActor(source);
        var prepared = await PrepareCash(source, actor);
        await using var first = Context();
        await using var second = Context();
        async Task<int> Pay(BillingDbContext db)
        {
            try
            {
                var service = CashPaymentServiceTests.Fixture.MakeService(db, actor, Service(db, source, actor), new CashPaymentServiceTests.CheckoutSource());
                await service.PayCash(prepared.BillId, Guid.NewGuid().ToString(), prepared.Body, default);
                return 201;
            }
            catch (BillingException error)
            {
                return error.StatusCode;
            }
        }

        var results = await Task.WhenAll(Pay(first), Pay(second));
        Assert.Equal(new[] { 201, 409 }, results.Order().ToArray());
        await using var check = Context();
        Assert.Single(await check.Payments.ToListAsync());
        Assert.Equal(232, (await check.CashMovements.SingleAsync()).SignedAmount);
        Assert.Equal("Paid", (await check.Bills.SingleAsync()).Status);
        Assert.Null((await check.BillCheckouts.SingleAsync()).ReleasedAt);
    }

    [BillingSqlServerFact]
    public async Task ConcurrentCashRetriesWithSameKeyReturnSameReceipt()
    {
        var source = new BillServiceTests.BillSource();
        var actor = CashActor(source);
        var prepared = await PrepareCash(source, actor);
        var key = Guid.NewGuid().ToString();
        await using var first = Context();
        await using var second = Context();
        var results = await Task.WhenAll(
            CashPaymentServiceTests.Fixture.MakeService(first, actor, Service(first, source, actor), new CashPaymentServiceTests.CheckoutSource())
                .PayCash(prepared.BillId, key, prepared.Body, default),
            CashPaymentServiceTests.Fixture.MakeService(second, actor, Service(second, source, actor), new CashPaymentServiceTests.CheckoutSource())
                .PayCash(prepared.BillId, key, prepared.Body, default));
        Assert.Single(results, result => result.Created);
        Assert.Equal(results[0].Payment.PaymentId, results[1].Payment.PaymentId);
        await using var check = Context();
        Assert.Single(await check.Payments.ToListAsync());
        Assert.Single(await check.CashMovements.ToListAsync());
    }

    [BillingSqlServerFact]
    public async Task CashPaymentAndCheckoutCancellationCannotBothSucceed()
    {
        var source = new BillServiceTests.BillSource();
        var actor = CashActor(source);
        var prepared = await PrepareCash(source, actor);
        await using var paying = Context();
        await using var cancelling = Context();
        var remote = new CashPaymentServiceTests.CheckoutSource();
        var payment = CashPaymentServiceTests.Fixture.MakeService(paying, actor, Service(paying, source, actor), remote);
        var checkout = new BillCheckoutService(new BillCheckoutRepository(cancelling), new BillRepository(cancelling),
            new UnitOfWork<BillingDbContext>(cancelling), remote, source, Service(cancelling, source, actor), actor,
            Options.Create(new CashRegisterAccessSettings
            {
                CashierRoleIds = ["cashier"]
            }), TimeProvider.System, new TestPaymentRequestClient());
        async Task<int> Result(Func<Task> action, int success)
        {
            try
            {
                await action();
                return success;
            }
            catch (BillingException error)
            {
                return error.StatusCode;
            }
        }

        var results = await Task.WhenAll(Result(() => payment.PayCash(prepared.BillId, Guid.NewGuid().ToString(), prepared.Body, default), 201),
            Result(() => checkout.Cancel(source.Value.TableSessionId, prepared.Body.CheckoutId, default), 200));
        Assert.Contains(409, results);
        Assert.Single(results, status => status is 200 or 201);
        await using var check = Context();
        var state = await check.BillCheckouts.SingleAsync();
        var paid = await check.Payments.AnyAsync();
        Assert.Equal(paid ? "Paid" : "Released", state.Status);
        Assert.Equal(paid, !state.ReleasedAt.HasValue);
        Assert.Equal(paid ? 1 : 0, await check.CashMovements.CountAsync());
    }

    [BillingSqlServerFact]
    public async Task LedgerWriteFailureRollsBackReceiptAndAllowsSafeRetry()
    {
        var source = new BillServiceTests.BillSource();
        var actor = CashActor(source);
        var prepared = await PrepareCash(source, actor);
        var key = Guid.NewGuid().ToString();
        await Execute(_connection, "CREATE TRIGGER dbo.CashTestFailure ON dbo.CashMovement AFTER INSERT AS BEGIN THROW 51000, 'Test ledger failure', 1; END;");
        await using (var failing = Context())
        {
            var service = CashPaymentServiceTests.Fixture.MakeService(failing, actor, Service(failing, source, actor), new CashPaymentServiceTests.CheckoutSource());
            await Assert.ThrowsAsync<DbUpdateException>(() => service.PayCash(prepared.BillId, key, prepared.Body, default));
        }

        await using (var check = Context())
        {
            Assert.Empty(await check.Payments.ToListAsync());
            Assert.Empty(await check.CashMovements.ToListAsync());
            Assert.Equal("Unpaid", (await check.Bills.SingleAsync()).Status);
            Assert.Equal("Ready", (await check.BillCheckouts.SingleAsync()).Status);
        }

        await Execute(_connection, "DROP TRIGGER dbo.CashTestFailure;");
        await using var retrying = Context();
        var retry = CashPaymentServiceTests.Fixture.MakeService(retrying, actor, Service(retrying, source, actor), new CashPaymentServiceTests.CheckoutSource());
        Assert.True((await retry.PayCash(prepared.BillId, key, prepared.Body, default)).Created);
    }

    [BillingSqlServerFact]
    public async Task CashMethodSeedIsRepeatableAndDoesNotReenableDisabledCash()
    {
        var seed = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Sql", "BillingCashMethod.sql"));
        await Execute(_connection, seed);
        await Execute(_connection, seed);
        await Execute(_connection, "UPDATE dbo.PaymentMethod SET IsEnabled=0 WHERE MethodCode='Cash';");
        await Execute(_connection, seed);
        await using var db = Context();
        Assert.False((await db.PaymentMethods.SingleAsync()).IsEnabled);
        Assert.Empty(await db.Payments.ToListAsync());
    }

    private static ShiftIdentity CashActor(BillServiceTests.BillSource source)
    {
        return new ShiftIdentity
        {
            Scope = new BillingAccessScope
            {
                UserId = Guid.NewGuid(),
                RoleId = "cashier",
                RestaurantIds = [source.Value.RestaurantId]
            }
        };
    }

    private async Task<(Guid BillId, CreateCashPaymentDTO Body)> PrepareCash(BillServiceTests.BillSource source, ShiftIdentity actor)
    {
        await using var db = Context();
        var bill = await Service(db, source, actor).Create(source.Value.TableSessionId, default);
        var shiftId = Guid.NewGuid();
        var checkoutId = Guid.NewGuid();
        db.CashShifts.Add(new CashShift
        {
            CashShiftId = shiftId,
            CashRegister = new CashRegister
            {
                CashRegisterId = Guid.NewGuid(),
                RestaurantId = source.Value.RestaurantId,
                Code = "SQL-CASH"
            },
            CashierUserRef = actor.Scope.UserId,
            OpeningCash = 1000,
            Status = "Active",
            OpenedAt = DateTime.UtcNow
        });
        db.PaymentMethods.Add(new PaymentMethod
        {
            MethodCode = "Cash",
            IsEnabled = true
        });
        db.BillCheckouts.Add(new BillCheckout
        {
            CheckoutId = checkoutId,
            BillId = bill.BillId,
            TableSessionRef = source.Value.TableSessionId,
            Status = "Ready",
            SourceHash = bill.Items[0].Item.SourceFingerprint,
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        return (bill.BillId, new CreateCashPaymentDTO
        {
            CheckoutId = checkoutId,
            CashShiftId = shiftId,
            CashReceived = 300
        });
    }

    private static BillService Service(BillingDbContext db, BillServiceTests.BillSource source, ShiftIdentity? actor = null)
    {
        var identity = actor ?? new ShiftIdentity
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
