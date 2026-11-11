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

public sealed class ShiftReportSqlServerTests : IAsyncLifetime
{
    private const string Prefix = "CodexBillingClose_";
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
    public async Task ConcurrentSameInputsReturnOneReportAndSameClosedTime()
    {
        var source = new BillServiceTests.BillSource();
        var actor = CashActor(source);
        var p = await PrepareCash(source, actor);
        await using var one = Context();
        await using var two = Context();
        var results = await Task.WhenAll(
            ShiftReportServiceTests.Make(one, actor).Close(p.Body.CashShiftId, ShiftReportServiceTests.Body(1000, 500), default),
            ShiftReportServiceTests.Make(two, actor).Close(p.Body.CashShiftId, ShiftReportServiceTests.Body(1000, 500), default));
        Assert.Single(results, result => result.Created);
        Assert.Equal(results[0].Report.ShiftReportId, results[1].Report.ShiftReportId);
        await using var check = Context();
        Assert.Single(await check.ShiftReports.ToListAsync());
        Assert.Equal((await check.CashShifts.SingleAsync()).ClosedAt, (await check.ShiftReports.SingleAsync()).ClosedAt);
    }

    [BillingSqlServerFact]
    public async Task ConcurrentDifferentInputsCannotOverwriteWinner()
    {
        var source = new BillServiceTests.BillSource();
        var actor = CashActor(source);
        var p = await PrepareCash(source, actor);
        await using var one = Context();
        await using var two = Context();
        async Task<int> Close(BillingDbContext db, decimal actual)
        {
            try
            {
                await ShiftReportServiceTests.Make(db, actor).Close(p.Body.CashShiftId,
                    ShiftReportServiceTests.Body(actual, 0, "End count"), default);
                return 201;
            }
            catch (BillingException error)
            {
                return error.StatusCode;
            }
        }

        Assert.Equal(new[] { 201, 409 }, (await Task.WhenAll(Close(one, 1000), Close(two, 999))).Order().ToArray());
        await using var check = Context();
        Assert.Single(await check.ShiftReports.ToListAsync());
    }

    [BillingSqlServerFact]
    public async Task CloseAndManualPostingShareTheRegisterLock()
    {
        var source = new BillServiceTests.BillSource();
        var actor = CashActor(source);
        var p = await PrepareCash(source, actor);
        await using var closing = Context();
        await using var posting = Context();
        async Task<int> Post()
        {
            try
            {
                await CashMovementServiceTests.Make(posting, actor).Create(p.Body.CashShiftId, Guid.NewGuid().ToString(),
                    CashMovementServiceTests.Body("CashIn", 100), default);
                return 201;
            }
            catch (BillingException error)
            {
                return error.StatusCode;
            }
        }

        var close = ShiftReportServiceTests.Make(closing, actor).Close(p.Body.CashShiftId,
            ShiftReportServiceTests.Body(1000, 0, "End count"), default);
        var post = Post();
        await Task.WhenAll(close, post);
        Assert.Contains(post.Result, new[] { 201, 409 });
        Assert.Equal(post.Result == 201 ? 1100 : 1000, close.Result.Report.ExpectedCash);
        await using var check = Context();
        Assert.Equal(post.Result == 201 ? 1 : 0, await check.CashMovements.CountAsync());
        Assert.Equal("Closed", (await check.CashShifts.SingleAsync()).Status);
    }

    [BillingSqlServerFact]
    public async Task CloseAndCashPaymentCannotLeaveSaleOutsideTheSnapshot()
    {
        var source = new BillServiceTests.BillSource();
        var actor = CashActor(source);
        var p = await PrepareCash(source, actor, true);
        await using var closing = Context();
        await using var paying = Context();
        var closeTask = Close();
        var payTask = CashPaymentServiceTests.Fixture.MakeService(paying, actor, Service(paying, source, actor),
            new CashPaymentServiceTests.CheckoutSource()).PayCash(p.BillId, p.Body.CheckoutId.ToString(), p.Body, default);
        async Task<int> Close()
        {
            try
            {
                await ShiftReportServiceTests.Make(closing, actor).Close(p.Body.CashShiftId,
                    ShiftReportServiceTests.Body(1232, 0, "End count"), default);
                return 201;
            }
            catch (BillingException error)
            {
                return error.StatusCode;
            }
        }

        await Task.WhenAll(closeTask, payTask);
        Assert.True(payTask.Result.Created);
        Assert.Contains(closeTask.Result, new[] { 201, 409 });
        await using var check = Context();
        if (closeTask.Result == 409)
        {
            await ShiftReportServiceTests.Make(check, actor).Close(p.Body.CashShiftId,
                ShiftReportServiceTests.Body(1232, 0), default);
        }

        Assert.Equal("Paid", (await check.Payments.SingleAsync()).Status);
        Assert.Single(await check.CashMovements.ToListAsync());
        Assert.Equal(1232, (await check.ShiftReports.SingleAsync()).ExpectedCash);
    }

    [BillingSqlServerFact]
    public async Task ReportInsertFailureRollsBackShiftAndAllowsSafeRetry()
    {
        var source = new BillServiceTests.BillSource();
        var actor = CashActor(source);
        var p = await PrepareCash(source, actor);
        await Execute(_connection, "CREATE TRIGGER dbo.CloseTestFailure ON dbo.ShiftReport AFTER INSERT AS BEGIN THROW 51000, 'Test report write failure', 1; END;");
        await using (var failing = Context())
        {
            await Assert.ThrowsAsync<DbUpdateException>(() => ShiftReportServiceTests.Make(failing, actor)
                .Close(p.Body.CashShiftId, ShiftReportServiceTests.Body(1000, 0), default));
        }

        await using (var check = Context())
        {
            Assert.Empty(await check.ShiftReports.ToListAsync());
            Assert.Equal("Active", (await check.CashShifts.SingleAsync()).Status);
            Assert.Null((await check.CashShifts.SingleAsync()).ClosedAt);
        }

        await Execute(_connection, "DROP TRIGGER dbo.CloseTestFailure;");
        await using var retry = Context();
        Assert.True((await ShiftReportServiceTests.Make(retry, actor).Close(p.Body.CashShiftId,
            ShiftReportServiceTests.Body(1000, 0), default)).Created);
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

    private async Task<(Guid BillId, CreateCashPaymentDTO Body)> PrepareCash(BillServiceTests.BillSource source, ShiftIdentity actor, bool withHandover = false)
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
        if (withHandover)
        {
            var checkout = db.BillCheckouts.Local.Single();
            checkout.CashRegisterId = db.CashShifts.Local.Single().CashRegister.CashRegisterId;
            checkout.SubmittedAt = DateTime.UtcNow;
            checkout.PreparedByRef = actor.Scope.UserId;
            checkout.PreparedAt = DateTime.UtcNow;
            checkout.PresentedAt = DateTime.UtcNow;
            CashPaymentServiceTests.Fixture.SeedPending(db, bill.BillId, shiftId, checkoutId, bill.TotalAmount);
        }

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
