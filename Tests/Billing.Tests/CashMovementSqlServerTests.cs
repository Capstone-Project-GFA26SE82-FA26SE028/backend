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

public sealed class CashMovementSqlServerTests : IAsyncLifetime
{
    private const string Prefix = "CodexBillingLedger_";
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
    public async Task ConcurrentWithdrawalsCannotOverdrawShift()
    {
        var source = new BillServiceTests.BillSource();
        var actor = CashActor(source);
        var prepared = await PrepareCash(source, actor);
        await using var one = Context();
        await using var two = Context();
        async Task<int> Post(BillingDbContext db)
        {
            try
            {
                await CashMovementServiceTests.Make(db, actor).Create(prepared.Body.CashShiftId,
                    Guid.NewGuid().ToString(), CashMovementServiceTests.Body("CashOut", 800), default);
                return 201;
            }
            catch (BillingException error)
            {
                return error.StatusCode;
            }
        }

        Assert.Equal(new[] { 201, 409 }, (await Task.WhenAll(Post(one), Post(two))).Order().ToArray());
        await using var check = Context();
        var ledger = await CashMovementServiceTests.Make(check, actor).GetAll(prepared.Body.CashShiftId, 1, 20, default);
        Assert.Equal(200, ledger.ExpectedCash);
        Assert.Single(ledger.Movements.Items);
    }

    [BillingSqlServerFact]
    public async Task ConcurrentSameKeyReturnsOneMovement()
    {
        var source = new BillServiceTests.BillSource();
        var actor = CashActor(source);
        var prepared = await PrepareCash(source, actor);
        var key = Guid.NewGuid().ToString();
        await using var one = Context();
        await using var two = Context();
        var results = await Task.WhenAll(
            CashMovementServiceTests.Make(one, actor).Create(prepared.Body.CashShiftId, key, CashMovementServiceTests.Body("CashIn", 100), default),
            CashMovementServiceTests.Make(two, actor).Create(prepared.Body.CashShiftId, key, CashMovementServiceTests.Body("CashIn", 100), default));
        Assert.Single(results, result => result.Created);
        Assert.Equal(results[0].Movement.CashMovementId, results[1].Movement.CashMovementId);
        await using var check = Context();
        Assert.Single(await check.CashMovements.ToListAsync());
    }

    [BillingSqlServerFact]
    public async Task CashPaymentAndWithdrawalUseSameShiftSerialization()
    {
        var source = new BillServiceTests.BillSource();
        var actor = CashActor(source);
        var prepared = await PrepareCash(source, actor);
        await using var paying = Context();
        await using var posting = Context();
        var payment = CashPaymentServiceTests.Fixture.MakeService(paying, actor, Service(paying, source, actor), new CashPaymentServiceTests.CheckoutSource());
        async Task<int> Withdraw()
        {
            try
            {
                await CashMovementServiceTests.Make(posting, actor).Create(prepared.Body.CashShiftId,
                    Guid.NewGuid().ToString(), CashMovementServiceTests.Body("CashOut", 1200), default);
                return 201;
            }
            catch (BillingException error)
            {
                return error.StatusCode;
            }
        }

        var pay = payment.PayCash(prepared.BillId, Guid.NewGuid().ToString(), prepared.Body, default);
        var withdrawal = Withdraw();
        await Task.WhenAll(pay, withdrawal);
        Assert.True(pay.Result.Created);
        Assert.Contains(withdrawal.Result, new[] { 201, 409 });
        await using var check = Context();
        var ledger = await CashMovementServiceTests.Make(check, actor).GetAll(prepared.Body.CashShiftId, 1, 20, default);
        Assert.Equal(withdrawal.Result == 201 ? 32 : 1232, ledger.ExpectedCash);
        Assert.Single(ledger.Movements.Items, value => value.Type == "Sale");
        Assert.Single(await check.Payments.ToListAsync());
    }

    [BillingSqlServerFact]
    public async Task WriteFailureRollsBackAndRetryPostsExactlyOnce()
    {
        var source = new BillServiceTests.BillSource();
        var actor = CashActor(source);
        var prepared = await PrepareCash(source, actor);
        var key = Guid.NewGuid().ToString();
        await Execute(_connection, "CREATE TRIGGER dbo.LedgerTestFailure ON dbo.CashMovement AFTER INSERT AS BEGIN THROW 51000, 'Test write failure', 1; END;");
        await using (var failed = Context())
        {
            await Assert.ThrowsAsync<DbUpdateException>(() => CashMovementServiceTests.Make(failed, actor).Create(
                prepared.Body.CashShiftId, key, CashMovementServiceTests.Body("CashOut", 100), default));
        }

        await using (var check = Context())
        {
            Assert.Empty(await check.CashMovements.ToListAsync());
        }

        await Execute(_connection, "DROP TRIGGER dbo.LedgerTestFailure;");
        await using var retry = Context();
        Assert.True((await CashMovementServiceTests.Make(retry, actor).Create(prepared.Body.CashShiftId,
            key, CashMovementServiceTests.Body("CashOut", 100), default)).Created);
    }

    [BillingSqlServerFact]
    public async Task ConcurrentCashInCannotExceedReportRange()
    {
        await VerifyConcurrentBalanceLimit(false);
    }

    [BillingSqlServerFact]
    public async Task ConcurrentCashInAndSaleCannotExceedReportRange()
    {
        await VerifyConcurrentBalanceLimit(true);
    }

    private async Task VerifyConcurrentBalanceLimit(bool includeSale)
    {
        var source = new BillServiceTests.BillSource();
        var actor = CashActor(source);
        var prepared = await PrepareCash(source, actor);
        const decimal opening = 9999999999999767m;
        await using (var setup = Context())
        {
            var shift = await setup.CashShifts.SingleAsync();
            shift.OpeningCash = opening;
            await setup.SaveChangesAsync();
        }

        await using var one = Context();
        await using var two = Context();
        async Task<int> Post(BillingDbContext db, bool sale)
        {
            try
            {
                if (sale)
                {
                    await CashPaymentServiceTests.Fixture.MakeService(db, actor, Service(db, source, actor),
                        new CashPaymentServiceTests.CheckoutSource()).PayCash(prepared.BillId,
                        Guid.NewGuid().ToString(), prepared.Body, default);
                }
                else
                {
                    await CashMovementServiceTests.Make(db, actor).Create(prepared.Body.CashShiftId,
                        Guid.NewGuid().ToString(), CashMovementServiceTests.Body("CashIn", 232), default);
                }

                return 201;
            }
            catch (BillingException error)
            {
                return error.StatusCode;
            }
        }

        var results = await Task.WhenAll(Post(one, false), Post(two, includeSale));
        Assert.Equal(new[] { 201, 409 }, results.Order().ToArray());
        await using var check = Context();
        var movement = Assert.Single(await check.CashMovements.ToListAsync());
        Assert.Equal(opening, (await check.CashShifts.SingleAsync()).OpeningCash);
        Assert.Equal(232m, movement.SignedAmount);
        Assert.Equal(9999999999999999m, opening + movement.SignedAmount);
        var paid = movement.Type == "Sale";
        Assert.Equal(paid ? 1 : 0, await check.Payments.CountAsync());
        Assert.Equal(paid ? "Paid" : "Unpaid", (await check.Bills.SingleAsync()).Status);
        Assert.Equal(paid ? "Paid" : "Ready", (await check.BillCheckouts.SingleAsync()).Status);
    }

    [BillingSqlServerFact]
    public async Task SaleOverLimitLeavesBillCheckoutAndLedgerUnchanged()
    {
        var source = new BillServiceTests.BillSource();
        var actor = CashActor(source);
        var prepared = await PrepareCash(source, actor);
        await using (var setup = Context())
        {
            var shift = await setup.CashShifts.SingleAsync();
            shift.OpeningCash = 9999999999999768m;
            await setup.SaveChangesAsync();
        }

        await using var paying = Context();
        var service = CashPaymentServiceTests.Fixture.MakeService(paying, actor, Service(paying, source, actor),
            new CashPaymentServiceTests.CheckoutSource());
        var error = await Assert.ThrowsAsync<BillingException>(() => service.PayCash(prepared.BillId,
            Guid.NewGuid().ToString(), prepared.Body, default));
        Assert.Equal(409, error.StatusCode);
        await using var check = Context();
        Assert.Empty(await check.Payments.ToListAsync());
        Assert.Empty(await check.CashMovements.ToListAsync());
        Assert.Equal("Unpaid", (await check.Bills.SingleAsync()).Status);
        Assert.Equal("Ready", (await check.BillCheckouts.SingleAsync()).Status);
        Assert.Equal(9999999999999768m, (await check.CashShifts.SingleAsync()).OpeningCash);
    }

    [BillingSqlServerFact]
    public async Task ExistingOverLimitBalanceIsRejectedWithoutRewritingHistory()
    {
        var source = new BillServiceTests.BillSource();
        var actor = CashActor(source);
        var prepared = await PrepareCash(source, actor);
        await using (var setup = Context())
        {
            var shift = await setup.CashShifts.SingleAsync();
            shift.OpeningCash = 9999999999999999m;
            setup.CashMovements.Add(new CashMovement
            {
                CashMovementId = Guid.NewGuid(),
                CashShiftId = shift.CashShiftId,
                Type = "CashIn",
                SignedAmount = 1,
                Reason = "Legacy over-limit fixture",
                PostedAt = DateTime.UtcNow,
                IdempotencyKey = Guid.NewGuid().ToString()
            });
            await setup.SaveChangesAsync();
        }

        await using var posting = Context();
        var error = await Assert.ThrowsAsync<BillingException>(() => CashMovementServiceTests.Make(posting, actor)
            .Create(prepared.Body.CashShiftId, Guid.NewGuid().ToString(),
                CashMovementServiceTests.Body("CashIn", 1), default));
        Assert.Equal(409, error.StatusCode);
        await using var check = Context();
        Assert.Equal(9999999999999999m, (await check.CashShifts.SingleAsync()).OpeningCash);
        Assert.Equal(1m, (await check.CashMovements.SingleAsync()).SignedAmount);
        Assert.Empty(await check.Payments.ToListAsync());
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
            CashRegisterId = db.CashShifts.Local.Single().CashRegister.CashRegisterId,
            SubmittedAt = DateTime.UtcNow,
            PreparedByRef = actor.Scope.UserId,
            PreparedAt = DateTime.UtcNow,
            PresentedAt = DateTime.UtcNow,
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
            }), TimeProvider.System, new TestPaymentRequestClient());
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
