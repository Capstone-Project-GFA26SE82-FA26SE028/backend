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

public sealed class PayOsSqlServerTests : IAsyncLifetime
{
    private const string Prefix = "CodexBillingPayOs_";
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
    public async Task ConcurrentCreateAndConcurrentSettlementProduceOnePaidRecord()
    {
        var source = new BillServiceTests.BillSource();
        var actor = CashActor(source);
        var prepared = await PrepareCash(source, actor);
        var provider = new PayOsPaymentServiceTests.FakeProvider();
        var key = Guid.NewGuid().ToString();
        async Task<(PayOsPaymentDTO Payment, bool Created)> Create()
        {
            await using var db = Context();
            return await Make(db, source, actor, provider).Create(prepared.BillId, key, new CreatePayOsPaymentDTO
            {
                CheckoutId = prepared.Body.CheckoutId,
                CashShiftId = prepared.Body.CashShiftId
            }, default);
        }

        var attempts = await Task.WhenAll(Create(), Create());
        Assert.Single(attempts, value => value.Created);
        Assert.Equal(attempts[0].Payment.PaymentId, attempts[1].Payment.PaymentId);
        Assert.Equal(1, provider.CreateCalls);
        provider.Settle(232);
        async Task<PayOsPaymentDTO> Settle()
        {
            await using var db = Context();
            return await Make(db, source, actor, provider).Synchronize(attempts[0].Payment.PaymentId, default);
        }

        await Task.WhenAll(Settle(), Settle());
        await using var check = Context();
        Assert.Equal("Paid", (await check.Payments.SingleAsync()).Status);
        Assert.Equal("Paid", (await check.Bills.SingleAsync()).Status);
        Assert.Equal("Paid", (await check.BillCheckouts.SingleAsync()).Status);
        Assert.Empty(await check.CashMovements.ToListAsync());
    }

    [BillingSqlServerFact]
    public async Task UpgradeIsRepeatablePreservesDisabledMethodAndExistingPayment()
    {
        var source = new BillServiceTests.BillSource();
        var actor = CashActor(source);
        var prepared = await PrepareCash(source, actor);
        await using (var db = Context())
        {
            db.Payments.Add(new Payment
            {
                PaymentId = Guid.NewGuid(),
                BillId = prepared.BillId,
                CashShiftId = prepared.Body.CashShiftId,
                MethodCode = "BankTransfer",
                Status = "Pending",
                Amount = 232,
                IdempotencyKey = "legacy-test"
            });
            (await db.PaymentMethods.SingleAsync()).IsEnabled = false;
            await db.SaveChangesAsync();
        }

        await Execute(_connection, "ALTER TABLE dbo.Payment DROP COLUMN ProviderPaymentLinkId, ProviderCheckoutUrl, ProviderQrCode;");
        var upgrade = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Sql", "BillingPayOs.sql"));
        await Execute(_connection, upgrade);
        await Execute(_connection, upgrade);
        await using var check = Context();
        Assert.False((await check.PaymentMethods.SingleAsync()).IsEnabled);
        var payment = await check.Payments.SingleAsync();
        Assert.Null(payment.ProviderCheckoutUrl);
        Assert.Null(payment.ProviderQrCode);
        Assert.Null(payment.ProviderPaymentLinkId);
        Assert.Equal(232, payment.Amount);
    }

    [BillingSqlServerFact]
    public async Task DifferentKeysCannotCreateTwoOutstandingAttemptsForOneBill()
    {
        var source = new BillServiceTests.BillSource();
        var actor = CashActor(source);
        var prepared = await PrepareCash(source, actor);
        var provider = new PayOsPaymentServiceTests.FakeProvider();
        async Task<int> Create()
        {
            await using var db = Context();
            try
            {
                await Make(db, source, actor, provider).Create(prepared.BillId, Guid.NewGuid().ToString(), new CreatePayOsPaymentDTO
                {
                    CheckoutId = prepared.Body.CheckoutId,
                    CashShiftId = prepared.Body.CashShiftId
                }, default);
                return 201;
            }
            catch (BillingException error)
            {
                return error.StatusCode;
            }
        }

        var results = await Task.WhenAll(Create(), Create());
        Assert.Single(results, value => value == 201);
        Assert.Single(results, value => value == 409);
        await using var check = Context();
        Assert.Single(await check.Payments.ToListAsync());
    }

    [BillingSqlServerFact]
    public async Task WebhookAndReconcileSerializeAndDedupeOnSqlServer()
    {
        var source = new BillServiceTests.BillSource();
        var actor = CashActor(source);
        var prepared = await PrepareCash(source, actor);
        var provider = new PayOsPaymentServiceTests.FakeProvider();
        Guid paymentId;
        await using (var creating = Context())
        {
            paymentId = (await Make(creating, source, actor, provider).Create(prepared.BillId, Guid.NewGuid().ToString(), new CreatePayOsPaymentDTO
            {
                CheckoutId = prepared.Body.CheckoutId,
                CashShiftId = prepared.Body.CashShiftId
            }, default)).Payment.PaymentId;
        }

        provider.Settle(232);
        provider.Webhook = new PayOsWebhookDTO(provider.Value!.OrderCode, 232, "BANK-001", "link-001", "VND", "00", new string('a', 64), new string('b', 64));
        async Task Deliver()
        {
            await using var db = Context();
            var service = new PaymentWebhookService(new PaymentRepository(db), new PaymentWebhookRepository(db),
                new BillRepository(db), new CashRegisterRepository(db), new CashShiftRepository(db), new UnitOfWork<BillingDbContext>(db),
                provider, Make(db, source, actor, provider), TimeProvider.System);
            await service.Process("signed-test", default);
        }

        async Task Reconcile()
        {
            await using var db = Context();
            await Make(db, source, actor, provider).Synchronize(paymentId, default);
        }

        await Task.WhenAll(Deliver(), Deliver(), Reconcile());
        await using var check = Context();
        Assert.Equal("Paid", (await check.Payments.SingleAsync()).Status);
        Assert.Single(await check.PaymentWebhooks.ToListAsync());
        Assert.Equal("Applied", (await check.PaymentWebhooks.SingleAsync()).ProcessingStatus);
        Assert.Null((await check.BillCheckouts.SingleAsync()).ReleasedAt);
    }

    [BillingSqlServerFact]
    public async Task FailedSettlementWriteRollsBackAndNewServiceRecoversSameAttempt()
    {
        var source = new BillServiceTests.BillSource();
        var actor = CashActor(source);
        var prepared = await PrepareCash(source, actor);
        var provider = new PayOsPaymentServiceTests.FakeProvider();
        Guid paymentId;
        await using (var creating = Context())
        {
            paymentId = (await Make(creating, source, actor, provider).Create(prepared.BillId, Guid.NewGuid().ToString(), new CreatePayOsPaymentDTO
            {
                CheckoutId = prepared.Body.CheckoutId,
                CashShiftId = prepared.Body.CashShiftId
            }, default)).Payment.PaymentId;
        }

        provider.Settle(232);
        await Execute(_connection, "CREATE TRIGGER dbo.PayOsTestFailure ON dbo.Payment AFTER UPDATE AS BEGIN IF EXISTS(SELECT 1 FROM inserted WHERE Status='Paid') THROW 51000, 'Test settlement failure', 1; END;");
        await using (var failing = Context())
        {
            await Assert.ThrowsAsync<DbUpdateException>(() => Make(failing, source, actor, provider).Synchronize(paymentId, default));
        }

        await using (var check = Context())
        {
            Assert.Equal("Unpaid", (await check.Bills.SingleAsync()).Status);
            Assert.Equal("Pending", (await check.Payments.SingleAsync()).Status);
            Assert.Equal("Ready", (await check.BillCheckouts.SingleAsync()).Status);
        }

        await Execute(_connection, "DROP TRIGGER dbo.PayOsTestFailure;");
        await using var recovered = Context();
        Assert.Equal("Paid", (await Make(recovered, source, actor, provider).Synchronize(paymentId, default)).Status);
        Assert.Single(await recovered.Payments.ToListAsync());
    }

    private static PayOsPaymentService Make(BillingDbContext db, BillServiceTests.BillSource source, ShiftIdentity actor,
        PayOsPaymentServiceTests.FakeProvider provider)
    {
        return new PayOsPaymentService(new PaymentRepository(db), new BillRepository(db), new BillCheckoutRepository(db),
            new CashShiftRepository(db), new CashRegisterRepository(db), new PaymentMethodRepository(db),
            new PaymentWebhookRepository(db), new UnitOfWork<BillingDbContext>(db), actor, Service(db, source, actor),
            new CashPaymentServiceTests.CheckoutSource(), provider, Options.Create(new CashRegisterAccessSettings
            {
                CashierRoleIds = ["cashier"],
                ManagerRoleIds = ["manager"]
            }), Options.Create(new PayOsSettings
            {
                Enabled = true
            }), TimeProvider.System);
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
            MethodCode = "BankTransfer",
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
