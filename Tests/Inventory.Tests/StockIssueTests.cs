using Backend.Contracts.Inventory;
using Grpc.Core;
using Inventory.API.GrpcServices;
using Inventory.BLL.Interfaces;
using Inventory.BLL.Services;
using Inventory.Common;
using Inventory.Common.DTOs;
using Inventory.DAL;
using Inventory.DAL.Models;
using Inventory.DAL.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Shared.Persistence.RestaurantIsolation;
using Shared.Persistence.UnitOfWorks;
using Xunit;

namespace Inventory.Tests;

public sealed class StockIssueTests
{
    [Fact]
    public async Task FifoSplitsAcrossLotsAndReplayNeverIssuesTwice()
    {
        using var f = new Fixture();
        var ingredient = await f.Ingredient();
        var first = await f.Lot(ingredient, 2, new DateOnly(2026, 12, 1), -2);
        var second = await f.Lot(ingredient, 5, new DateOnly(2026, 11, 1), -1);
        var request = Request(ingredient, 3);
        var issue = await f.Issues.Create(request, default);
        Assert.Equal(-2, issue.Movements.Single(x => x.GoodsReceiptItemId == first).QuantityChange);
        Assert.Equal(-1, issue.Movements.Single(x => x.GoodsReceiptItemId == second).QuantityChange);
        f.Db.ChangeTracker.Clear();
        request.Items[0].Quantity = 3.000000m;
        await f.Issues.Create(request, default);
        Assert.Equal(4, (await f.Db.Ingredients.SingleAsync()).StockQuantity);
        Assert.Equal(2, await f.Db.StockMovements.CountAsync(x => x.MovementType == "Issue"));
        var page = await f.Lots.GetAll(ingredient.IngredientId, 1, 20, default);
        Assert.Equal(0, page.Items.Single(x => x.GoodsReceiptItemId == first).RemainingQuantity);
        Assert.Equal(4, page.Items.Single(x => x.GoodsReceiptItemId == second).RemainingQuantity);
        Assert.Equal(issue.StockIssueId, (await f.Issues.GetById(issue.StockIssueId, default)).StockIssueId);
    }

    [Fact]
    public async Task ExpiredUnknownAndDraftLotsAreExcludedButTodayIsUsable()
    {
        using var f = new Fixture();
        var ingredient = await f.Ingredient();
        await f.Lot(ingredient, 50, new DateOnly(2026, 10, 21), -4);
        await f.Lot(ingredient, 50, null, -3);
        await f.Lot(ingredient, 50, new DateOnly(2027, 1, 1), -2, draft: true);
        var today = await f.Lot(ingredient, 2, new DateOnly(2026, 10, 22), -1);
        var issue = await f.Issues.Create(Request(ingredient, 2), default);
        Assert.Equal(today, Assert.Single(issue.Movements).GoodsReceiptItemId);
        await Error(409, () => f.Issues.Create(Request(ingredient, 1), default));
        f.Db.ChangeTracker.Clear();
        var page = await f.Lots.GetAll(ingredient.IngredientId, 1, 20, default);
        Assert.Equal(3, page.TotalCount);
        Assert.Contains(page.Items, x => x.ExpiryStatus == "Expired" && !x.CanIssue);
        Assert.Contains(page.Items, x => x.ExpiryStatus == "Unknown" && !x.CanIssue);
        Assert.Contains(page.Items, x => x.ExpiryStatus == "ExpiringSoon" && x.RemainingQuantity == 0);
        Assert.Equal(100, (await f.Db.Ingredients.SingleAsync()).StockQuantity);
    }

    [Fact]
    public async Task LaterLineFailureRollsBackEntireIssue()
    {
        using var f = new Fixture();
        var first = await f.Ingredient(Guid.Parse("00000000-0000-0000-0000-000000000001"));
        var second = await f.Ingredient(Guid.Parse("00000000-0000-0000-0000-000000000002"));
        await f.Lot(first, 10, new DateOnly(2027, 1, 1), -2);
        await f.Lot(second, 1, new DateOnly(2027, 1, 1), -1);
        var request = Request(first, 2);
        request.Items.Add(new StockIssueLineDTO
        {
            IngredientId = second.IngredientId,
            Quantity = 2
        });
        await Error(409, () => f.Issues.Create(request, default));
        f.Db.ChangeTracker.Clear();
        Assert.Equal(10, (await f.Db.Ingredients.FindAsync(first.IngredientId))!.StockQuantity);
        Assert.Empty(await f.Db.StockIssues.ToListAsync());
        Assert.Empty(await f.Db.StockMovements.Where(x => x.MovementType == "Issue").ToListAsync());
    }

    [Fact]
    public async Task ReusedIdWithChangedBodyOrActorIsRejected()
    {
        using var f = new Fixture();
        var ingredient = await f.Ingredient();
        await f.Lot(ingredient, 10, new DateOnly(2027, 1, 1), -1);
        var request = Request(ingredient, 1);
        await f.Issues.Create(request, default);
        request.Items[0].Quantity = 2;
        await Error(409, () => f.Issues.Create(request, default));
        request.Items[0].Quantity = 1;
        request.Reason = "Another reason";
        await Error(409, () => f.Issues.Create(request, default));
        request.Reason = "Kitchen preparation";
        f.Access.Actor = Guid.NewGuid();
        await Error(409, () => f.Issues.Create(request, default));
        Assert.Equal(9, ingredient.StockQuantity);
    }

    [Fact]
    public async Task UnallocatedStockAndInactiveIngredientsCannotBeIssued()
    {
        using var f = new Fixture();
        var ingredient = await f.Ingredient();
        ingredient.StockQuantity = 100;
        await f.Db.SaveChangesAsync();
        await Error(409, () => f.Issues.Create(Request(ingredient, 1), default));
        await f.Lot(ingredient, 10, new DateOnly(2027, 1, 1), -1);
        ingredient.IsActive = false;
        await f.Db.SaveChangesAsync();
        await Error(409, () => f.Issues.Create(Request(ingredient, 1), default));
        Assert.False(Assert.Single((await f.Lots.GetAll(ingredient.IngredientId, 1, 20, default)).Items).CanIssue);
    }

    [Fact]
    public async Task PriorMovementsReduceAvailableLotBalance()
    {
        using var f = new Fixture();
        var ingredient = await f.Ingredient();
        await f.Lot(ingredient, 10, new DateOnly(2027, 1, 1), -1);
        await f.Issues.Create(Request(ingredient, 8), default);
        await Error(409, () => f.Issues.Create(Request(ingredient, 3), default));
        await f.Issues.Create(Request(ingredient, 2), default);
        Assert.Equal(0, ingredient.StockQuantity);
        Assert.Equal(0, Assert.Single((await f.Lots.GetAll(ingredient.IngredientId, 1, 20, default)).Items).RemainingQuantity);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("0.0000001")]
    [InlineData("1000000000000")]
    public async Task InvalidQuantitiesAreRejected(string quantity)
    {
        using var f = new Fixture();
        var ingredient = await f.Ingredient();
        var request = Request(ingredient, decimal.Parse(quantity, System.Globalization.CultureInfo.InvariantCulture));
        await Error(400, () => f.Issues.Create(request, default));
        Assert.Empty(await f.Db.StockIssues.ToListAsync());
    }

    [Fact]
    public async Task DuplicateIngredientsAndMissingReasonAreRejected()
    {
        using var f = new Fixture();
        var ingredient = await f.Ingredient();
        var request = Request(ingredient, 1);
        request.Items.Add(request.Items[0]);
        await Error(400, () => f.Issues.Create(request, default));
        request.Items.RemoveAt(1);
        request.Reason = " ";
        await Error(400, () => f.Issues.Create(request, default));
    }

    [Fact]
    public async Task EveryOperationRequiresAccess()
    {
        using var f = new Fixture();
        f.Access.Denied = true;
        await Error(403, () => f.Issues.Create(new(), default));
        await Error(403, () => f.Issues.GetById(Guid.NewGuid(), default));
        await Error(403, () => f.Lots.GetAll(Guid.NewGuid(), 1, 20, default));
    }

    [Fact]
    public void ExpiryUsesVietnamDateAtMidnightBoundary()
    {
        Assert.Equal(new DateOnly(2026, 10, 23), StockLotService.BusinessDate(new Clock
        {
            Now = new DateTimeOffset(2026, 10, 22, 17, 0, 0, TimeSpan.Zero)
        }));
    }

    [Fact]
    public async Task ExpiredLotCanBeDisposedWhenLotIdAndDisposalSpecified()
    {
        using var f = new Fixture();
        var ingredient = await f.Ingredient();
        var expiredLotId = await f.Lot(ingredient, 50, new DateOnly(2026, 10, 20), -5);
        var request = new StockIssueWriteDTO
        {
            StockIssueId = Guid.NewGuid(),
            Reason = "Hủy lô gạo hết hạn",
            Items =
            [
                new StockIssueLineDTO
                {
                    IngredientId = ingredient.IngredientId,
                    GoodsReceiptItemId = expiredLotId,
                    Quantity = 30,
                    IsDisposal = true
                }
            ]
        };

        var issue = await f.Issues.Create(request, default);

        Assert.Equal("Disposal", Assert.Single(issue.Movements).MovementType);
        Assert.Equal(expiredLotId, Assert.Single(issue.Movements).GoodsReceiptItemId);
        Assert.Equal(-30, Assert.Single(issue.Movements).QuantityChange);
        Assert.Equal(20, (await f.Db.Ingredients.FindAsync(ingredient.IngredientId))!.StockQuantity);

        var page = await f.Lots.GetAll(ingredient.IngredientId, 1, 20, default);
        Assert.Equal(20, Assert.Single(page.Items).RemainingQuantity);
    }

    [Fact]
    public async Task DisposingMoreThanLotRemainingQuantityThrows409()
    {
        using var f = new Fixture();
        var ingredient = await f.Ingredient();
        var expiredLotId = await f.Lot(ingredient, 50, new DateOnly(2026, 10, 20), -5);
        var request = new StockIssueWriteDTO
        {
            StockIssueId = Guid.NewGuid(),
            Reason = "Hủy lô gạo hết hạn",
            Items =
            [
                new StockIssueLineDTO
                {
                    IngredientId = ingredient.IngredientId,
                    GoodsReceiptItemId = expiredLotId,
                    Quantity = 55,
                    IsDisposal = true
                }
            ]
        };

        await Error(409, () => f.Issues.Create(request, default));
    }

    [Fact]
    public async Task TargetingExpiredLotWithoutDisposalFlagThrows409()
    {
        using var f = new Fixture();
        var ingredient = await f.Ingredient();
        var expiredLotId = await f.Lot(ingredient, 50, new DateOnly(2026, 10, 20), -5);
        var request = new StockIssueWriteDTO
        {
            StockIssueId = Guid.NewGuid(),
            Reason = "Xuất sử dụng thông thường",
            Items =
            [
                new StockIssueLineDTO
                {
                    IngredientId = ingredient.IngredientId,
                    GoodsReceiptItemId = expiredLotId,
                    Quantity = 10,
                    IsDisposal = false
                }
            ]
        };

        await Error(409, () => f.Issues.Create(request, default));
    }

    [Fact]
    public async Task DisposingExpiredLotForInactiveIngredientIsAllowed()
    {
        using var f = new Fixture();
        var ingredient = await f.Ingredient();
        var expiredLotId = await f.Lot(ingredient, 50, new DateOnly(2026, 10, 20), -5);
        ingredient.IsActive = false;
        await f.Db.SaveChangesAsync();

        var request = new StockIssueWriteDTO
        {
            StockIssueId = Guid.NewGuid(),
            Reason = "Hủy lô nguyên liệu đã ngừng kinh doanh",
            Items =
            [
                new StockIssueLineDTO
                {
                    IngredientId = ingredient.IngredientId,
                    GoodsReceiptItemId = expiredLotId,
                    Quantity = 50,
                    IsDisposal = true
                }
            ]
        };

        var issue = await f.Issues.Create(request, default);

        Assert.Equal("Disposal", Assert.Single(issue.Movements).MovementType);
        Assert.Equal(0, (await f.Db.Ingredients.FindAsync(ingredient.IngredientId))!.StockQuantity);
    }

    [Fact]
    public async Task GetByIdIncludesCreatorNameAndLotMetadataAndEnforcesRestaurantScope()
    {
        var restaurantId = Guid.NewGuid();
        var otherRestaurantId = Guid.NewGuid();
        using var f = new Fixture(restaurantId);
        f.StaffProfiles.Names[(restaurantId, f.Access.Actor)] = "Nguyễn Văn A";

        var ingredient = await f.Ingredient();
        var firstLotId = await f.Lot(ingredient, 2, new DateOnly(2026, 12, 1), -3);
        var secondLotId = await f.Lot(ingredient, 5, new DateOnly(2026, 11, 15), -1);
        var created = await f.Issues.Create(Request(ingredient, 3), default);

        f.Db.ChangeTracker.Clear();
        var grpc = new StockIssueGrpcService(f.Issues);
        var reply = await grpc.GetById(new InventoryIdRequest
        {
            Id = created.StockIssueId.ToString()
        }, new TestCallContext());

        Assert.Equal(created.StockIssueId.ToString(), reply.Id);
        Assert.Equal(f.Access.Actor.ToString(), reply.CreatedBy);
        Assert.True(reply.HasCreatedByName);
        Assert.Equal("Nguyễn Văn A", reply.CreatedByName);
        Assert.Equal(2, reply.Allocations.Count);

        var firstAllocation = Assert.Single(reply.Allocations, x => x.ReceiptItemId == firstLotId.ToString());
        Assert.Equal("2026-12-01", firstAllocation.ExpiryDate);
        Assert.Equal("2026-10-19T08:00:00.0000000Z", firstAllocation.ReceivedAt);

        var secondAllocation = Assert.Single(reply.Allocations, x => x.ReceiptItemId == secondLotId.ToString());
        Assert.Equal("2026-11-15", secondAllocation.ExpiryDate);
        Assert.Equal("2026-10-21T08:00:00.0000000Z", secondAllocation.ReceivedAt);

        using var otherDb = f.OpenForRestaurant(otherRestaurantId, out var otherIssues);
        await Error(404, () => otherIssues.GetById(created.StockIssueId, default));
    }

    [Fact]
    public async Task GetByIdReturnsNullMetadataWhenCreatorMissingOrMovementHasNoLot()
    {
        var restaurantId = Guid.NewGuid();
        using var f = new Fixture(restaurantId);
        var ingredient = await f.Ingredient();
        var lotWithoutExpiry = await f.Lot(ingredient, 5, null, -2);
        var issueId = Guid.NewGuid();
        var issue = new StockIssue
        {
            StockIssueId = issueId,
            CreatedByRef = Guid.NewGuid(),
            CreatedAt = new DateTime(2026, 10, 22, 8, 0, 0, DateTimeKind.Utc),
            Reason = "Legacy issue",
            RequestHash = new string('a', 64),
            Movements =
            [
                new StockMovement
                {
                    StockMovementId = Guid.NewGuid(),
                    StockIssueId = issueId,
                    IngredientId = ingredient.IngredientId,
                    GoodsReceiptItemId = null,
                    MovementType = "Issue",
                    QuantityChange = -1,
                    PerformedByRef = f.Access.Actor,
                    Reason = "Unlinked legacy movement",
                    CreatedAt = new DateTime(2026, 10, 22, 8, 0, 0, DateTimeKind.Utc)
                },
                new StockMovement
                {
                    StockMovementId = Guid.NewGuid(),
                    StockIssueId = issueId,
                    IngredientId = ingredient.IngredientId,
                    GoodsReceiptItemId = lotWithoutExpiry,
                    MovementType = "Issue",
                    QuantityChange = -2,
                    PerformedByRef = f.Access.Actor,
                    Reason = "Lot without expiry",
                    CreatedAt = new DateTime(2026, 10, 22, 8, 0, 0, DateTimeKind.Utc)
                }
            ]
        };
        f.Db.Set<StockIssue>().Add(issue);
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();

        var grpc = new StockIssueGrpcService(f.Issues);
        var reply = await grpc.GetById(new InventoryIdRequest
        {
            Id = issueId.ToString()
        }, new TestCallContext());

        Assert.False(reply.HasCreatedByName);
        Assert.Equal(2, reply.Allocations.Count);

        var unlinked = Assert.Single(reply.Allocations, x => string.IsNullOrEmpty(x.ReceiptItemId));
        Assert.False(unlinked.HasReceivedAt);
        Assert.False(unlinked.HasExpiryDate);

        var linkedNoExpiry = Assert.Single(reply.Allocations, x => x.ReceiptItemId == lotWithoutExpiry.ToString());
        Assert.True(linkedNoExpiry.HasReceivedAt);
        Assert.Equal("2026-10-20T08:00:00.0000000Z", linkedNoExpiry.ReceivedAt);
        Assert.False(linkedNoExpiry.HasExpiryDate);
    }

    private static StockIssueWriteDTO Request(Ingredient ingredient, decimal quantity)
    {
        return new StockIssueWriteDTO
        {
            StockIssueId = Guid.NewGuid(),
            Reason = "Kitchen preparation",
            Items =
            [
                new StockIssueLineDTO
                {
                    IngredientId = ingredient.IngredientId,
                    Quantity = quantity
                }
            ]
        };
    }

    private static async Task Error(int status, Func<Task> action)
    {
        Assert.Equal(status, (await Assert.ThrowsAsync<InventoryException>(action)).StatusCode);
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 10, 22, 8, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow()
        {
            return Now;
        }
    }

    private sealed class Access : IInventoryAccess
    {
        public Guid Actor { get; set; } = Guid.NewGuid();
        public bool Denied { get; set; }

        public Task<Guid> RequireAsync(string operation, CancellationToken ct)
        {
            if (Denied)
            {
                throw new InventoryException(403, "Forbidden");
            }
            return Task.FromResult(Actor);
        }
    }

    private sealed class TestDb(DbContextOptions<InventoryDbContext> options, RestaurantDataScope? scope = null)
        : InventoryDbContext(options, scope)
    {
        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);
            builder.Entity<Ingredient>().Property(x => x.StockVersion).IsRowVersion().HasDefaultValue(new byte[8]);
        }
    }

    private sealed class FakeStaffProfiles : IInventoryStaffProfileClient
    {
        public Dictionary<(Guid RestaurantId, Guid UserId), string?> Names { get; } = new();

        public Task<string?> GetFullNameAsync(Guid restaurantId, Guid userId, CancellationToken ct)
        {
            Names.TryGetValue((restaurantId, userId), out var name);
            return Task.FromResult(name);
        }
    }

    private sealed class TestCallContext : ServerCallContext
    {
        protected override string MethodCore => "StockIssueGrpc/GetById";
        protected override string HostCore => "localhost";
        protected override string PeerCore => "localhost";
        protected override DateTime DeadlineCore => DateTime.UtcNow.AddMinutes(1);
        protected override Metadata RequestHeadersCore { get; } = [];
        protected override CancellationToken CancellationTokenCore => CancellationToken.None;
        protected override Metadata ResponseTrailersCore { get; } = [];
        protected override Status StatusCore { get; set; }
        protected override WriteOptions? WriteOptionsCore { get; set; }
        protected override AuthContext AuthContextCore => new(string.Empty, new Dictionary<string, List<AuthProperty>>());
        protected override ContextPropagationToken CreatePropagationTokenCore(ContextPropagationOptions? options) => throw new NotImplementedException();
        protected override Task WriteResponseHeadersAsyncCore(Metadata responseHeaders) => Task.CompletedTask;
    }

    private sealed class Fixture : IDisposable
    {
        private readonly SqliteConnection connection = new("Data Source=:memory:");
        private readonly Clock clock = new();
        public RestaurantDataScope Scope { get; } = new();
        public FakeStaffProfiles StaffProfiles { get; } = new();
        public InventoryDbContext Db { get; }
        public Access Access { get; } = new();
        public StockIssueService Issues { get; }
        public StockLotService Lots { get; }

        public Fixture(Guid? restaurantId = null)
        {
            if (restaurantId.HasValue)
            {
                Scope.Select(restaurantId.Value);
            }

            connection.Open();
            Db = new TestDb(
                new DbContextOptionsBuilder<InventoryDbContext>().UseSqlite(connection).Options,
                restaurantId.HasValue ? Scope : null);
            Db.Database.EnsureCreated();
            var transaction = new InventoryTransaction(new UnitOfWork<InventoryDbContext>(Db));
            var ingredients = new IngredientRepository(Db);
            var lots = new StockLotRepository(Db);
            Issues = new StockIssueService(new StockIssueRepository(Db), lots, ingredients, transaction, Access, clock, StaffProfiles, Scope);
            Lots = new StockLotService(lots, ingredients, Access, transaction, clock);
        }

        public InventoryDbContext OpenForRestaurant(Guid otherRestaurantId, out StockIssueService otherIssues)
        {
            var otherScope = new RestaurantDataScope();
            otherScope.Select(otherRestaurantId);
            var otherDb = new TestDb(new DbContextOptionsBuilder<InventoryDbContext>().UseSqlite(connection).Options, otherScope);
            var otherTransaction = new InventoryTransaction(new UnitOfWork<InventoryDbContext>(otherDb));
            otherIssues = new StockIssueService(
                new StockIssueRepository(otherDb),
                new StockLotRepository(otherDb),
                new IngredientRepository(otherDb),
                otherTransaction,
                Access,
                clock,
                StaffProfiles,
                otherScope);
            return otherDb;
        }

        public async Task<Ingredient> Ingredient(Guid? id = null)
        {
            var ingredient = new Ingredient
            {
                IngredientId = id ?? Guid.NewGuid(),
                IngredientCode = Guid.NewGuid().ToString("N"),
                IngredientName = "Rice",
                BaseUnit = "g",
                IsActive = true
            };
            Db.Ingredients.Add(ingredient);
            await Db.SaveChangesAsync();
            return ingredient;
        }

        public async Task<Guid> Lot(Ingredient ingredient, decimal quantity, DateOnly? expiry, int daysAgo, bool draft = false)
        {
            var id = Guid.NewGuid();
            var time = clock.GetUtcNow().AddDays(daysAgo).UtcDateTime;
            var receipt = new GoodsReceipt
            {
                GoodsReceiptId = Guid.NewGuid(),
                ReceiptCode = Guid.NewGuid().ToString("N"),
                Status = draft ? "Draft" : "Confirmed",
                CreatedByRef = Access.Actor,
                CreatedAt = time,
                ConfirmAt = draft ? null : time,
                GoodsReceiptItems =
                [
                    new GoodsReceiptItem
                    {
                        GoodsReceiptItemId = id,
                        IngredientId = ingredient.IngredientId,
                        ReceivedQuantity = quantity,
                        ExpiryDate = expiry
                    }
                ]
            };
            Db.GoodsReceipts.Add(receipt);
            if (!draft)
            {
                ingredient.StockQuantity += quantity;
                Db.StockMovements.Add(new StockMovement
                {
                    StockMovementId = Guid.NewGuid(),
                    IngredientId = ingredient.IngredientId,
                    GoodsReceiptItemId = id,
                    QuantityChange = quantity,
                    MovementType = "Receipt",
                    CreatedAt = time
                });
            }
            await Db.SaveChangesAsync();
            return id;
        }

        public void Dispose()
        {
            Db.Dispose();
            connection.Dispose();
        }
    }
}
