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

public sealed class BillServiceTests
{
    [Fact]
    public async Task CookingAndServingProgressDoesNotInvalidateFinancialSnapshot()
    {
        await using var fixture = await BillFixture.Create();
        var bill = await fixture.Service.Create(fixture.Source.Value.TableSessionId, default);
        fixture.Source.Value.Items[0].Item.Status = "Served";
        Assert.Equal("Current", (await fixture.Service.GetById(bill.BillId, default)).SourceStatus);
        fixture.Source.Value.PendingOrderCount = 1;
        Assert.Equal("Stale", (await fixture.Service.GetById(bill.BillId, default)).SourceStatus);
    }

    [Fact]
    public async Task CreationUsesOrderSnapshotsStoresUnknownCogsAndRoundsVndTotal()
    {
        await using var fixture = await BillFixture.Create();
        var bill = await fixture.Service.Create(fixture.Source.Value.TableSessionId, default);
        Assert.Equal(201.25m, bill.SubTotal);
        Assert.Equal(10.06m, bill.ServiceAmount);
        Assert.Equal(21.13m, bill.VatAmount);
        Assert.Equal(232m, bill.TotalAmount);
        Assert.Equal(-0.44m, bill.RoundingAdjustment);
        Assert.False(bill.HasCompleteCogs);
        Assert.Null(Assert.Single(bill.Items).CogsSnapshot);
        Assert.Equal("Unpaid", bill.Status);
        Assert.Equal("MatchedAtCreation", bill.SourceStatus);
        Assert.Contains(bill.Warnings, warning => warning.Contains("chưa được phục vụ"));
        Assert.Equal((double)bill.ServiceAmount, await fixture.Db.BillLines.SumAsync(line => (double)line.ServiceAllocated), 2);
        Assert.Empty(await fixture.Db.Payments.ToListAsync());
    }

    [Theory]
    [InlineData("admin", true)]
    [InlineData("manager", false)]
    [InlineData("waiter", false)]
    [InlineData("kitchen", false)]
    public async Task OtherRolesCannotCreate(string role, bool administrator)
    {
        await using var fixture = await BillFixture.Create();
        fixture.Actor.Scope.RoleId = role;
        fixture.Actor.Scope.IsAdministrator = administrator;
        await Status(403, () => fixture.Service.Create(fixture.Source.Value.TableSessionId, default));
        Assert.Empty(await fixture.Db.Bills.ToListAsync());
        Assert.Equal(0, fixture.Source.Calls);
    }

    [Fact]
    public async Task CrossBranchAndRevokedAssignmentsCannotCreateOrRead()
    {
        await using var fixture = await BillFixture.Create();
        var result = await fixture.Service.Create(fixture.Source.Value.TableSessionId, default);
        fixture.Actor.Scope.RestaurantIds = [Guid.NewGuid()];
        await Status(403, () => fixture.Service.GetById(result.BillId, default));
        await Status(403, () => fixture.Service.GetAll(new BillQueryDTO
        {
            RestaurantId = result.RestaurantId
        }, default));
        fixture.Source.Value.TableSessionId = Guid.NewGuid();
        await Status(403, () => fixture.Service.Create(fixture.Source.Value.TableSessionId, default));
        Assert.Single(await fixture.Db.Bills.ToListAsync());
    }

    [Theory]
    [InlineData("admin", true)]
    [InlineData("manager", false)]
    [InlineData("cashier", false)]
    public async Task ReadScopeMatchesRegisters(string role, bool administrator)
    {
        await using var fixture = await BillFixture.Create();
        var created = await fixture.Service.Create(fixture.Source.Value.TableSessionId, default);
        fixture.Actor.Scope.RoleId = role;
        fixture.Actor.Scope.IsAdministrator = administrator;
        var read = await fixture.Service.GetById(created.BillId, default);
        Assert.Equal("Current", read.SourceStatus);
        Assert.Single((await fixture.Service.GetAll(new BillQueryDTO(), default)).Items);
    }

    [Fact]
    public async Task MissingFutureOrAmbiguousPolicyCannotCreateBills()
    {
        await using var fixture = await BillFixture.Create();
        fixture.Db.TaxFeePolicies.RemoveRange(fixture.Db.TaxFeePolicies);
        await fixture.Db.SaveChangesAsync();
        await Status(409, () => fixture.Service.Create(fixture.Source.Value.TableSessionId, default));
        Assert.Empty(await fixture.Db.Bills.ToListAsync());
        fixture.Db.TaxFeePolicies.Add(Policy(DateTime.UtcNow.AddDays(1)));
        await fixture.Db.SaveChangesAsync();
        await Status(409, () => fixture.Service.Create(fixture.Source.Value.TableSessionId, default));
    }

    [Fact]
    public async Task DisabledServiceFeeDoesNotChargeItsConfiguredRate()
    {
        await using var fixture = await BillFixture.Create();
        var policy = await fixture.Db.TaxFeePolicies.SingleAsync();
        policy.ServiceEnabled = false;
        await fixture.Db.SaveChangesAsync();
        var bill = await fixture.Service.Create(fixture.Source.Value.TableSessionId, default);
        Assert.Equal(0, bill.ServiceAmount);
        Assert.Equal(0, bill.ServiceRateSnapshot);
        Assert.Equal(221, bill.TotalAmount);
    }

    [Fact]
    public async Task RepeatCreationDoesNotChargeItemsTwice()
    {
        await using var fixture = await BillFixture.Create();
        await fixture.Service.Create(fixture.Source.Value.TableSessionId, default);
        await Status(409, () => fixture.Service.Create(fixture.Source.Value.TableSessionId, default));
        Assert.Single(await fixture.Db.Bills.ToListAsync());
        Assert.Single(await fixture.Db.BillLines.ToListAsync());
    }

    [Fact]
    public async Task ChangedOrdersMarkBillStaleWithoutOverwritingAmounts()
    {
        await using var fixture = await BillFixture.Create();
        var bill = await fixture.Service.Create(fixture.Source.Value.TableSessionId, default);
        fixture.Source.Value.Items[0].Quantity++;
        var read = await fixture.Service.GetById(bill.BillId, default);
        Assert.Equal("Stale", read.SourceStatus);
        Assert.Equal(bill.TotalAmount, read.TotalAmount);
        Assert.Equal(bill.Items[0].Quantity, read.Items[0].Quantity);
        var policy = await fixture.Db.TaxFeePolicies.SingleAsync();
        policy.VatRate = 0.2m;
        await fixture.Db.SaveChangesAsync();
        Assert.Equal(bill.VatRateSnapshot, (await fixture.Service.GetById(bill.BillId, default)).VatRateSnapshot);
    }

    [Fact]
    public async Task ChangedSnapshotDuringCreationRollsBack()
    {
        await using var fixture = await BillFixture.Create();
        fixture.Source.ChangeOnSecondRead = true;
        await Status(409, () => fixture.Service.Create(fixture.Source.Value.TableSessionId, default));
        Assert.Empty(await fixture.Db.Bills.ToListAsync());
        Assert.Empty(await fixture.Db.BillLines.ToListAsync());
    }

    [Fact]
    public async Task PendingAndEmptySessionsCannotProduceIncompleteBills()
    {
        await using var fixture = await BillFixture.Create();
        fixture.Source.Value.PendingOrderCount = 1;
        await Status(409, () => fixture.Service.Create(fixture.Source.Value.TableSessionId, default));
        fixture.Source.Value.PendingOrderCount = 0;
        fixture.Source.Value.Items.Clear();
        await Status(409, () => fixture.Service.Create(fixture.Source.Value.TableSessionId, default));
    }

    [Fact]
    public async Task MissingOriginalTableOrDuplicateSourceIdsAreNotInvented()
    {
        await using var fixture = await BillFixture.Create();
        var line = fixture.Source.Value.Items[0];
        line.OriginalTableId = Guid.Empty;
        await Status(503, () => fixture.Service.Create(fixture.Source.Value.TableSessionId, default));
        line.OriginalTableId = Guid.NewGuid();
        fixture.Source.Value.Items.Add(line);
        await Status(503, () => fixture.Service.Create(fixture.Source.Value.TableSessionId, default));
    }

    [Fact]
    public async Task PriceBasisMustBeExplicit()
    {
        await using var fixture = await BillFixture.Create();
        fixture.Settings.OrderPriceBasis = string.Empty;
        await Status(409, () => fixture.Service.Create(fixture.Source.Value.TableSessionId, default));
    }

    [Theory]
    [InlineData(503)]
    [InlineData(409)]
    [InlineData(404)]
    public async Task UnavailableSourceRetainsBillWithoutClaimingItIsCurrent(int status)
    {
        await using var fixture = await BillFixture.Create();
        var bill = await fixture.Service.Create(fixture.Source.Value.TableSessionId, default);
        fixture.Source.Error = new BillingException(status, "Source unavailable.");
        var read = await fixture.Service.GetById(bill.BillId, default);
        Assert.Equal("Unavailable", read.SourceStatus);
        Assert.Equal(bill.TotalAmount, read.TotalAmount);
        Assert.Contains(read.Warnings, warning => warning.Contains("tính cập nhật"));
    }

    private static TaxFeePolicy Policy(DateTime effective)
    {
        return new TaxFeePolicy
        {
            TaxFeePolicyId = Guid.NewGuid(),
            Version = 1,
            VatRate = 0.1m,
            ServiceRate = 0.05m,
            ServiceEnabled = true,
            EffectiveFrom = effective
        };
    }

    private static async Task Status(int expected, Func<Task> action)
    {
        Assert.Equal(expected, (await Assert.ThrowsAsync<BillingException>(action)).StatusCode);
    }

    internal sealed class BillFixture : IAsyncDisposable
    {
        private readonly SqliteConnection connection;
        public BillTestDb Db { get; }
        public ShiftIdentity Actor { get; } = new();
        public BillSource Source { get; } = new();
        public BillSettings Settings { get; } = new()
        {
            Currency = "VND",
            OrderPriceBasis = "Exclusive"
        };
        public BillService Service { get; }

        private BillFixture(SqliteConnection connection)
        {
            this.connection = connection;
            Db = new BillTestDb(new DbContextOptionsBuilder<BillingDbContext>().UseSqlite(connection).Options);
            Actor.Scope = new BillingAccessScope
            {
                UserId = Guid.NewGuid(),
                RoleId = "cashier",
                RestaurantIds = [Source.Value.RestaurantId]
            };
            Service = new BillService(new BillRepository(Db), new TaxFeePolicyRepository(Db), new UnitOfWork<BillingDbContext>(Db),
                Actor, Source, Options.Create(new CashRegisterAccessSettings
                {
                    CashierRoleIds = ["cashier"],
                    ManagerRoleIds = ["manager"]
                }), Options.Create(Settings), TimeProvider.System);
        }

        public static async Task<BillFixture> Create()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var fixture = new BillFixture(connection);
            await fixture.Db.Database.EnsureCreatedAsync();
            fixture.Db.TaxFeePolicies.Add(Policy(DateTime.UtcNow.AddDays(-1)));
            await fixture.Db.SaveChangesAsync();
            return fixture;
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await connection.DisposeAsync();
        }
    }

    internal sealed class BillTestDb(DbContextOptions<BillingDbContext> options) : BillingDbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<Bill>().Property(bill => bill.Version).HasDefaultValueSql("X'0000000000000000'");
        }
    }

    internal sealed class BillSource : IBillSourceClient
    {
        public int Calls { get; private set; }
        public bool ChangeOnSecondRead { get; set; }
        public int? ChangeOnCall { get; set; }
        public BillingException? Error { get; set; }
        public BillSourceDTO Value { get; } = new()
        {
            TableSessionId = Guid.NewGuid(),
            RestaurantId = Guid.NewGuid(),
            Items =
            [
                new BillSourceLineDTO
                {
                    OrderItemId = Guid.NewGuid(),
                    OriginalTableId = Guid.NewGuid(),
                    Quantity = 1,
                    Item = new BillItemSnapshot
                    {
                        OrderId = Guid.NewGuid(),
                        ItemId = Guid.NewGuid(),
                        Name = "Test dish",
                        BaseUnitPrice = 200.25m,
                        Status = "Ready",
                        Options =
                        [
                            new BillOptionSnapshot
                            {
                                OptionId = Guid.NewGuid(),
                                Name = "Extra",
                                ExtraPrice = 1,
                                Quantity = 1
                            }
                        ]
                    }
                }
            ]
        };

        public Task<BillSourceDTO> GetBySessionAsync(Guid sessionId, CancellationToken ct)
        {
            Calls++;
            if (Error is not null)
            {
                throw Error;
            }

            if ((ChangeOnSecondRead && Calls == 2) || Calls == ChangeOnCall)
            {
                Value.Items[0].Quantity++;
            }

            return Task.FromResult(Value);
        }
    }
}
