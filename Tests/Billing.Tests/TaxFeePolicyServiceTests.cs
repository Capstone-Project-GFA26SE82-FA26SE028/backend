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

public sealed class TaxFeePolicyServiceTests
{
    [Fact]
    public async Task AdministratorWithoutBranchAssignmentCreatesImmediatelyEffectivePolicy()
    {
        await using var fixture = await PolicyFixture.Create();
        var result = await fixture.Service.Create(Request(), default);
        Assert.NotEqual(Guid.Empty, result.TaxFeePolicyId);
        Assert.Equal(1, result.Version);
        Assert.Equal(0.08m, result.VatRate);
        Assert.Equal(0.05m, result.ServiceRate);
        Assert.True(result.ServiceEnabled);
        Assert.Equal(TimeSpan.Zero, result.EffectiveFrom.Offset);
        Assert.InRange(fixture.Clock.Now - result.EffectiveFrom, TimeSpan.Zero, TimeSpan.FromMilliseconds(4));
        Assert.Equal(result.TaxFeePolicyId, Assert.Single(await new TaxFeePolicyRepository(fixture.Db)
            .GetEffectiveAsync(fixture.Clock.Now.UtcDateTime, default)).TaxFeePolicyId);
        Assert.Empty(fixture.Access.Scope.RestaurantIds);
    }

    [Theory]
    [InlineData("manager")]
    [InlineData("cashier")]
    [InlineData("waiter")]
    [InlineData("kitchen")]
    [InlineData("inventory")]
    [InlineData("admin")]
    public async Task AdministratorFlagIsRequiredForAllOperations(string role)
    {
        await using var fixture = await PolicyFixture.Create();
        fixture.Access.Scope.RoleId = role;
        fixture.Access.Scope.IsAdministrator = false;
        fixture.Access.Scope.RestaurantIds = [Guid.NewGuid()];
        await Status(403, () => fixture.Service.Create(Request(), default));
        await Status(403, () => fixture.Service.GetAll(new TaxFeePolicyQueryDTO(), default));
        await Status(403, () => fixture.Service.GetById(Guid.NewGuid(), default));
        Assert.Empty(await fixture.Db.TaxFeePolicies.ToListAsync());
    }

    [Fact]
    public async Task InvalidEmployeeCannotReadOrCreate()
    {
        await using var fixture = await PolicyFixture.Create();
        fixture.Access.Scope.UserId = Guid.Empty;
        await Status(401, () => fixture.Service.Create(Request(), default));
        await Status(401, () => fixture.Service.GetAll(new TaxFeePolicyQueryDTO(), default));
        await Status(401, () => fixture.Service.GetById(Guid.NewGuid(), default));
    }

    [Theory]
    [InlineData("vat")]
    [InlineData("service")]
    [InlineData("enabled")]
    public async Task MissingFieldsAreNotConvertedToZeroOrFalse(string field)
    {
        await using var fixture = await PolicyFixture.Create();
        var request = Request();
        if (field == "vat")
        {
            request.VatRate = null;
        }
        else if (field == "service")
        {
            request.ServiceRate = null;
        }
        else
        {
            request.ServiceEnabled = null;
        }

        await Status(400, () => fixture.Service.Create(request, default));
        Assert.Empty(await fixture.Db.TaxFeePolicies.ToListAsync());
    }

    [Theory]
    [InlineData("-0.01")]
    [InlineData("1.000001")]
    [InlineData("8")]
    [InlineData("0.0000001")]
    public async Task InvalidRatesAreRejectedWithoutRounding(string value)
    {
        await using var fixture = await PolicyFixture.Create();
        var rate = decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
        var request = Request();
        request.VatRate = rate;
        await Status(400, () => fixture.Service.Create(request, default));
        request.VatRate = 0.08m;
        request.ServiceRate = rate;
        await Status(400, () => fixture.Service.Create(request, default));
        Assert.Empty(await fixture.Db.TaxFeePolicies.ToListAsync());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task ExplicitBoundaryRatesAreAllowed(int value)
    {
        await using var fixture = await PolicyFixture.Create();
        var request = Request();
        request.VatRate = value;
        request.ServiceRate = value;
        request.ServiceEnabled = false;
        var result = await fixture.Service.Create(request, default);
        Assert.Equal(value, result.VatRate);
        Assert.Equal(value, result.ServiceRate);
        Assert.False(result.ServiceEnabled);
    }

    [Theory]
    [InlineData("")]
    [InlineData("2026-10-28T14:00:00")]
    [InlineData("2026-10-26T14:00:00Z")]
    [InlineData("1752-12-31T23:59:59Z")]
    [InlineData("9999-12-31T23:59:59.999Z")]
    [InlineData("null")]
    public async Task InvalidOrBackdatedEffectiveTimesAreRejected(string value)
    {
        await using var fixture = await PolicyFixture.Create();
        var request = Request();
        request.EffectiveFrom = value;
        await Status(400, () => fixture.Service.Create(request, default));
        Assert.Empty(await fixture.Db.TaxFeePolicies.ToListAsync());
    }

    [Fact]
    public async Task OffsetAndSqlPrecisionAreNormalizedAndCannotProduceAmbiguousPolicies()
    {
        await using var fixture = await PolicyFixture.Create();
        var first = Request();
        first.EffectiveFrom = "2026-10-28T21:00:00.0001+07:00";
        var result = await fixture.Service.Create(first, default);
        Assert.Equal(new DateTimeOffset(2026, 10, 28, 14, 0, 0, TimeSpan.Zero), result.EffectiveFrom);
        var second = Request();
        second.EffectiveFrom = "2026-10-28T14:00:00.0002Z";
        await Status(409, () => fixture.Service.Create(second, default));
        Assert.Single(await fixture.Db.TaxFeePolicies.ToListAsync());
    }

    [Fact]
    public async Task FuturePolicyDoesNotApplyBeforeItsTimeAndVersionIsAnAppendSequence()
    {
        await using var fixture = await PolicyFixture.Create();
        var future = Request();
        future.EffectiveFrom = "2026-10-28T14:00:00Z";
        var scheduled = await fixture.Service.Create(future, default);
        var repository = new TaxFeePolicyRepository(fixture.Db);
        Assert.Empty(await repository.GetEffectiveAsync(fixture.Clock.Now.UtcDateTime, default));
        var immediate = await fixture.Service.Create(Request(), default);
        Assert.Equal(1, scheduled.Version);
        Assert.Equal(2, immediate.Version);
        Assert.Equal(immediate.TaxFeePolicyId, Assert.Single(await repository.GetEffectiveAsync(fixture.Clock.Now.UtcDateTime, default)).TaxFeePolicyId);
        Assert.Equal(scheduled.TaxFeePolicyId, Assert.Single(await repository.GetEffectiveAsync(scheduled.EffectiveFrom.UtcDateTime, default)).TaxFeePolicyId);
        var page = await fixture.Service.GetAll(new TaxFeePolicyQueryDTO
        {
            PageSize = 1
        }, default);
        Assert.Equal(2, page.TotalCount);
        Assert.Equal(scheduled.TaxFeePolicyId, Assert.Single(page.Items).TaxFeePolicyId);
    }

    [Fact]
    public async Task MissingIdsPaginationAndVersionOverflowAreControlled()
    {
        await using var fixture = await PolicyFixture.Create();
        await Status(400, () => fixture.Service.GetById(Guid.Empty, default));
        await Status(404, () => fixture.Service.GetById(Guid.NewGuid(), default));
        await Status(400, () => fixture.Service.GetAll(new TaxFeePolicyQueryDTO
        {
            Page = 0
        }, default));
        await Status(400, () => fixture.Service.GetAll(new TaxFeePolicyQueryDTO
        {
            PageSize = 101
        }, default));
        fixture.Db.TaxFeePolicies.Add(new TaxFeePolicy
        {
            TaxFeePolicyId = Guid.NewGuid(),
            Version = int.MaxValue,
            EffectiveFrom = fixture.Clock.Now.UtcDateTime.AddDays(-1)
        });
        await fixture.Db.SaveChangesAsync();
        await Status(409, () => fixture.Service.Create(Request(), default));
        Assert.Single(await fixture.Db.TaxFeePolicies.ToListAsync());
    }

    [Fact]
    public async Task NewPoliciesDoNotRewriteExistingBillAndDisabledFeeIsNotCharged()
    {
        await using var fixture = await PolicyFixture.Create();
        var first = Request();
        first.VatRate = 0.1m;
        await fixture.Service.Create(first, default);
        var firstSource = new BillServiceTests.BillSource();
        var billService = fixture.Bills(firstSource);
        var bill = await billService.Create(firstSource.Value.TableSessionId, default);
        Assert.Equal(232m, bill.TotalAmount);
        var saved = await fixture.Db.Bills.SingleAsync();
        saved.Status = "Paid";
        saved.PaidAt = fixture.Clock.Now.UtcDateTime;
        await fixture.Db.SaveChangesAsync();
        fixture.Clock.Now = fixture.Clock.Now.AddHours(1);
        var second = Request();
        second.VatRate = 0.2m;
        second.ServiceEnabled = false;
        var policy = await fixture.Service.Create(second, default);
        Assert.Equal(0.05m, policy.ServiceRate);
        Assert.False(policy.ServiceEnabled);
        var read = await billService.GetById(bill.BillId, default);
        Assert.Equal("Paid", read.Status);
        Assert.Equal(0.1m, read.VatRateSnapshot);
        Assert.Equal(0.05m, read.ServiceRateSnapshot);
        Assert.Equal(232m, read.TotalAmount);
        var nextSource = new BillServiceTests.BillSource();
        var next = await fixture.Bills(nextSource).Create(nextSource.Value.TableSessionId, default);
        Assert.Equal(0.2m, next.VatRateSnapshot);
        Assert.Equal(0m, next.ServiceAmount);
        Assert.Equal(0m, next.ServiceRateSnapshot);
        Assert.Equal(242m, next.TotalAmount);
        Assert.Empty(await fixture.Db.Payments.ToListAsync());
    }

    [Fact]
    public async Task AdministratorIsRecheckedAfterAcquiringTheWriteLock()
    {
        await using var fixture = await PolicyFixture.Create();
        var access = new RevokedAdministrator();
        var service = new TaxFeePolicyService(new TaxFeePolicyRepository(fixture.Db),
            new UnitOfWork<BillingDbContext>(fixture.Db), access, fixture.Clock);
        await Status(403, () => service.Create(Request(), default));
        Assert.Empty(await fixture.Db.TaxFeePolicies.ToListAsync());
    }

    internal static CreateTaxFeePolicyDTO Request()
    {
        return new CreateTaxFeePolicyDTO
        {
            VatRate = 0.08m,
            ServiceRate = 0.05m,
            ServiceEnabled = true
        };
    }

    private static async Task Status(int expected, Func<Task> action)
    {
        Assert.Equal(expected, (await Assert.ThrowsAsync<BillingException>(action)).StatusCode);
    }

    internal sealed class PolicyFixture : IAsyncDisposable
    {
        private readonly SqliteConnection connection;
        public BillServiceTests.BillTestDb Db { get; }
        public ShiftIdentity Access { get; } = new()
        {
            Scope = new BillingAccessScope
            {
                UserId = Guid.NewGuid(),
                RoleId = "admin",
                IsAdministrator = true
            }
        };
        public PolicyClock Clock { get; } = new();
        public TaxFeePolicyService Service { get; }

        private PolicyFixture(SqliteConnection connection)
        {
            this.connection = connection;
            Db = new BillServiceTests.BillTestDb(new DbContextOptionsBuilder<BillingDbContext>().UseSqlite(connection).Options);
            Service = new TaxFeePolicyService(new TaxFeePolicyRepository(Db), new UnitOfWork<BillingDbContext>(Db), Access, Clock);
        }

        public static async Task<PolicyFixture> Create()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var fixture = new PolicyFixture(connection);
            await fixture.Db.Database.EnsureCreatedAsync();
            return fixture;
        }

        public BillService Bills(BillServiceTests.BillSource source)
        {
            var cashier = new ShiftIdentity
            {
                Scope = new BillingAccessScope
                {
                    UserId = Guid.NewGuid(),
                    RoleId = "cashier",
                    RestaurantIds = [source.Value.RestaurantId]
                }
            };
            return new BillService(new BillRepository(Db), new TaxFeePolicyRepository(Db), new UnitOfWork<BillingDbContext>(Db),
                cashier, source, Options.Create(new CashRegisterAccessSettings
                {
                    CashierRoleIds = ["cashier"],
                    ManagerRoleIds = ["manager"]
                }), Options.Create(new BillSettings
                {
                    Currency = "VND",
                    OrderPriceBasis = "Exclusive"
                }), Clock, new TestPaymentRequestClient());
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await connection.DisposeAsync();
        }
    }

    internal sealed class PolicyClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 10, 27, 14, 0, 0, 1, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow()
        {
            return Now;
        }
    }

    private sealed class RevokedAdministrator : IBillingAccess
    {
        private readonly Guid user = Guid.NewGuid();
        private int calls;

        public Task<BillingAccessScope> GetCurrentScopeAsync(Guid? restaurantId, CancellationToken ct)
        {
            calls++;
            return Task.FromResult(new BillingAccessScope
            {
                UserId = user,
                RoleId = calls == 1 ? "admin" : "manager",
                IsAdministrator = calls == 1
            });
        }
    }
}
