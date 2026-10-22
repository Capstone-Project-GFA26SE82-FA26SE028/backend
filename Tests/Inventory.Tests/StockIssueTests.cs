using Inventory.BLL.Interfaces;
using Inventory.BLL.Services;
using Inventory.Common;
using Inventory.Common.DTOs;
using Inventory.DAL;
using Inventory.DAL.Models;
using Inventory.DAL.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
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

    private sealed class TestDb(DbContextOptions<InventoryDbContext> options) : InventoryDbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);
            builder.Entity<Ingredient>().Property(x => x.StockVersion).IsRowVersion().HasDefaultValue(new byte[8]);
        }
    }

    private sealed class Fixture : IDisposable
    {
        private readonly SqliteConnection connection = new("Data Source=:memory:");
        private readonly Clock clock = new();
        public InventoryDbContext Db { get; }
        public Access Access { get; } = new();
        public StockIssueService Issues { get; }
        public StockLotService Lots { get; }

        public Fixture()
        {
            connection.Open();
            Db = new TestDb(new DbContextOptionsBuilder<InventoryDbContext>().UseSqlite(connection).Options);
            Db.Database.EnsureCreated();
            var transaction = new InventoryTransaction(new UnitOfWork<InventoryDbContext>(Db));
            var ingredients = new IngredientRepository(Db);
            var lots = new StockLotRepository(Db);
            Issues = new StockIssueService(new StockIssueRepository(Db), lots, ingredients, transaction, Access, clock);
            Lots = new StockLotService(lots, ingredients, Access, transaction, clock);
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
