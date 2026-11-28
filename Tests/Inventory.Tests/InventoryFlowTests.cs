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

public sealed class InventoryFlowTests
{
    [Fact]
    public async Task ReceiptUsesConversionSnapshotAndRetryDoesNotRecalculate()
    {
        using var f = new Fixture();
        var ingredient = await f.AddIngredient();
        var kg = new Unit
        {
            UnitId = Guid.NewGuid(),
            Code = "kg",
            Name = "Kilogram"
        };
        f.Db.Units.Add(kg);
        await f.Db.SaveChangesAsync();
        await f.Conversions.Update(ingredient.IngredientId, kg.UnitId, 1000, default);
        var request = Receipt(ingredient.IngredientId);
        request.Items[0].UnitId = kg.UnitId;
        request.Items[0].ReceivedQuantity = 10;
        var receipt = await f.Receipts.Create(request, default);
        var line = Assert.Single(receipt.GoodsReceiptItems);
        Assert.Equal(10000, line.ReceivedQuantity);
        Assert.Equal(10, line.InputQuantity);
        Assert.Equal(1000, line.ConversionFactorSnapshot);
        await f.Conversions.Update(ingredient.IngredientId, kg.UnitId, 2000, default);
        await f.Receipts.Create(request, default);
        await f.Receipts.Confirm(receipt.GoodsReceiptId, default);
        Assert.Equal(10000, ingredient.StockQuantity);
        Assert.Equal(10000, Assert.Single(await f.Db.StockMovements.ToListAsync()).QuantityChange);
        request.Items[0].UnitId = null;
        await Error(409, () => f.Receipts.Create(request, default));
    }

    [Fact]
    public async Task ConversionIsIngredientSpecificAndBaseFactorCannotChange()
    {
        using var f = new Fixture();
        var ingredient = await f.AddIngredient();
        var gram = await f.Db.Units.SingleAsync();
        Assert.Equal(1, await f.Conversions.Resolve(ingredient.IngredientId, gram.UnitId, default));
        await Error(409, () => f.Conversions.Update(ingredient.IngredientId, gram.UnitId, 2, default));
        var bag = new Unit
        {
            UnitId = Guid.NewGuid(),
            Code = "bag",
            Name = "Bag"
        };
        f.Db.Units.Add(bag);
        await f.Db.SaveChangesAsync();
        await Error(409, () => f.Conversions.Resolve(ingredient.IngredientId, bag.UnitId, default));
        await f.Conversions.Update(ingredient.IngredientId, bag.UnitId, 25000, default);
        var other = await f.AddIngredient();
        await Error(409, () => f.Conversions.Resolve(other.IngredientId, bag.UnitId, default));
        await f.Ingredients.SetStatus(ingredient.IngredientId, false, default);
        await Error(409, () => f.Conversions.Resolve(ingredient.IngredientId, bag.UnitId, default));
    }

    [Fact]
    public async Task ConvertedQuantityCannotOverflowOrSilentlyRound()
    {
        using var f = new Fixture();
        var ingredient = await f.AddIngredient();
        var unit = new Unit
        {
            UnitId = Guid.NewGuid(),
            Code = "portion",
            Name = "Portion"
        };
        f.Db.Units.Add(unit);
        await f.Db.SaveChangesAsync();
        var request = Receipt(ingredient.IngredientId);
        request.Items[0].UnitId = unit.UnitId;
        request.Items[0].ReceivedQuantity = 0.000001m;
        await f.Conversions.Update(ingredient.IngredientId, unit.UnitId, 0.000001m, default);
        await Error(400, () => f.Receipts.Create(request, default));
        await f.Conversions.Update(ingredient.IngredientId, unit.UnitId, 999999999999m, default);
        request.Items[0].ReceivedQuantity = 999999999999m;
        await Error(400, () => f.Receipts.Create(request, default));
        Assert.Empty(await f.Db.GoodsReceipts.ToListAsync());
    }

    [Fact]
    public async Task UnitCreationRequiresManagerAndUniqueCode()
    {
        using var f = new Fixture();
        var service = new UnitService(new UnitRepository(f.Db), new InventoryTransaction(new UnitOfWork<InventoryDbContext>(f.Db)), f.Access);
        var unit = await service.Create(" KG ", "Kilogram", default);
        Assert.Equal("kg", unit.Code);
        await Error(409, () => service.Create("kg", "Duplicate", default));
        await Error(400, () => f.Ingredients.Create(new IngredientWriteDTO
        {
            IngredientName = "Unknown unit",
            BaseUnit = "missing"
        }, default));
        f.Access.Denied = true;
        await Error(403, () => service.Create("l", "Liter", default));
        await Error(403, () => service.GetAll(default));
        await Error(403, () => f.Conversions.GetAll(Guid.NewGuid(), default));
        await Error(403, () => f.Conversions.Update(Guid.NewGuid(), unit.UnitId, 1, default));
        await Error(403, () => f.Conversions.Resolve(Guid.NewGuid(), unit.UnitId, default));
    }

    [Fact]
    public async Task ReceiptDraftDoesNotChangeStockAndConfirmReplayAddsStockOnlyOnce()
    {
        using var f = new Fixture();
        var ingredient = await f.AddIngredient();
        var request = Receipt(ingredient.IngredientId);
        var draft = await f.Receipts.Create(request, default);
        Assert.Equal("Draft", draft.Status);
        Assert.Equal(0, ingredient.StockQuantity);
        Assert.Empty(await f.Db.StockMovements.ToListAsync());
        await f.Receipts.Create(request, default);
        await f.Receipts.Confirm(draft.GoodsReceiptId, default);
        f.Db.ChangeTracker.Clear();
        await f.Receipts.Confirm(draft.GoodsReceiptId, default);
        await f.Receipts.Create(request, default);
        Assert.Equal(12.5m, (await f.Ingredients.GetById(ingredient.IngredientId, default)).StockQuantity);
        var movement = Assert.Single(await f.Db.StockMovements.ToListAsync());
        Assert.Equal(12.5m, movement.QuantityChange);
        Assert.Equal(f.Access.Actor, movement.PerformedByRef);
        Assert.Equal(1, await f.Db.GoodsReceipts.CountAsync());
        Assert.Equal(1, (await f.Movements.GetAll(ingredient.IngredientId, 1, 20, default)).TotalCount);
        Assert.False((await f.Ingredients.GetAll(1, 20, null, null, true, default)).Items.Any());
    }

    [Fact]
    public async Task ReusingReceiptIdWithChangedDataOrActorIsRejected()
    {
        using var f = new Fixture();
        var ingredient = await f.AddIngredient();
        var request = Receipt(ingredient.IngredientId);
        await f.Receipts.Create(request, default);
        request.Items[0].ReceivedQuantity++;
        await Error(409, () => f.Receipts.Create(request, default));
        request.Items[0].ReceivedQuantity--;
        f.Access.Actor = Guid.NewGuid();
        await Error(409, () => f.Receipts.Create(request, default));
        Assert.Equal(1, await f.Db.GoodsReceipts.CountAsync());
    }

    [Fact]
    public async Task FailedConfirmationRollsBackAllLinesAndCanBeRetried()
    {
        using var f = new Fixture();
        var first = await f.AddIngredient();
        var second = await f.AddIngredient();
        var ordered = new[] { first, second }.OrderBy(x => x.IngredientId).ToArray();
        var request = Receipt(ordered[0].IngredientId);
        request.Items.Add(new ReceiptLineDTO
        {
            IngredientId = ordered[1].IngredientId,
            ReceivedQuantity = 2,
            ExpiryDate = new DateOnly(2030, 1, 1)
        });
        await f.Receipts.Create(request, default);
        await f.Ingredients.SetStatus(ordered[1].IngredientId, false, default);
        await Error(409, () => f.Receipts.Confirm(request.GoodsReceiptId, default));
        f.Db.ChangeTracker.Clear();
        Assert.All(await f.Db.Ingredients.ToListAsync(), x => Assert.Equal(0, x.StockQuantity));
        Assert.Empty(await f.Db.StockMovements.ToListAsync());
        Assert.Equal("Draft", (await f.Receipts.GetById(request.GoodsReceiptId, default)).Status);
        await f.Ingredients.SetStatus(ordered[1].IngredientId, true, default);
        await f.Receipts.Confirm(request.GoodsReceiptId, default);
        Assert.Equal(2, await f.Db.StockMovements.CountAsync());
    }

    [Fact]
    public async Task BaseUnitIsImmutableAndInactiveIngredientCannotBeReceived()
    {
        using var f = new Fixture();
        var ingredient = await f.AddIngredient();
        await Error(409, () => f.Ingredients.Update(ingredient.IngredientId, new IngredientWriteDTO
        {
            IngredientName = "Rice",
            BaseUnit = "kg"
        }, default));
        await f.Ingredients.Update(ingredient.IngredientId, new IngredientWriteDTO
        {
            IngredientName = " Rice renamed ",
            BaseUnit = "G",
            LowStockThreshold = 5
        }, default);
        await f.Ingredients.SetStatus(ingredient.IngredientId, false, default);
        await Error(409, () => f.Receipts.Create(Receipt(ingredient.IngredientId), default));
        var inactivePage = await f.Ingredients.GetAll(1, 20, "renamed", false, false, default);
        var inactiveItem = Assert.Single(inactivePage.Items);
        Assert.Equal("Rice renamed", inactiveItem.IngredientName);
        Assert.False(inactiveItem.IsLowStock);
        var lowStockPage = await f.Ingredients.GetAll(1, 20, "renamed", false, true, default);
        Assert.Equal(0, lowStockPage.TotalCount);
        Assert.Empty(lowStockPage.Items);
    }

    [Fact]
    public async Task LowStockUsesOnlyIssuableLotsExcludesInactiveAndPaginatesFilteredTotal()
    {
        var clock = new FixedClock(new DateTimeOffset(2026, 11, 28, 7, 0, 0, TimeSpan.FromHours(7)));
        var today = StockLotService.BusinessDate(clock);
        using var f = new Fixture(clock);
        var lon = new Unit
        {
            UnitId = Guid.NewGuid(),
            Code = "lon",
            Name = "Lon"
        };
        f.Db.Units.Add(lon);
        await f.Db.SaveChangesAsync();

        // 1. Pepsi: total stock 30 = 18 expired + 12 issuable, threshold 20 -> low stock, stockQuantity stays 30.
        var pepsi = await f.Ingredients.Create(new IngredientWriteDTO
        {
            IngredientName = "Lon Pepsi",
            BaseUnit = "lon",
            LowStockThreshold = 20
        }, default);
        var pepsiReceipt = await f.Receipts.Create(new ReceiptWriteDTO
        {
            GoodsReceiptId = Guid.NewGuid(),
            Note = "Pepsi lots",
            Items =
            [
                new ReceiptLineDTO
                {
                    IngredientId = pepsi.IngredientId,
                    ReceivedQuantity = 18,
                    ExpiryDate = today.AddDays(-1)
                },
                new ReceiptLineDTO
                {
                    IngredientId = pepsi.IngredientId,
                    ReceivedQuantity = 12,
                    ExpiryDate = today.AddDays(5)
                }
            ]
        }, default);
        await f.Receipts.Confirm(pepsiReceipt.GoodsReceiptId, default);

        // 2. All expired (0 issuable, threshold 0) -> still low stock, stockQuantity stays 10.
        var allExpired = await f.Ingredients.Create(new IngredientWriteDTO
        {
            IngredientName = "All Expired",
            BaseUnit = "g",
            LowStockThreshold = 0
        }, default);
        var expiredReceipt = await f.Receipts.Create(new ReceiptWriteDTO
        {
            GoodsReceiptId = Guid.NewGuid(),
            Note = "Expired only",
            Items =
            [
                new ReceiptLineDTO
                {
                    IngredientId = allExpired.IngredientId,
                    ReceivedQuantity = 10,
                    ExpiryDate = today.AddDays(-2)
                }
            ]
        }, default);
        await f.Receipts.Confirm(expiredReceipt.GoodsReceiptId, default);

        // 3. Partially issued down to exact threshold (25 - 10 = 15 == 15) -> low stock.
        var equalThreshold = await f.Ingredients.Create(new IngredientWriteDTO
        {
            IngredientName = "Equal Threshold",
            BaseUnit = "g",
            LowStockThreshold = 15
        }, default);
        var equalReceipt = await f.Receipts.Create(new ReceiptWriteDTO
        {
            GoodsReceiptId = Guid.NewGuid(),
            Note = "Valid lot",
            Items =
            [
                new ReceiptLineDTO
                {
                    IngredientId = equalThreshold.IngredientId,
                    ReceivedQuantity = 25,
                    ExpiryDate = today.AddDays(3)
                }
            ]
        }, default);
        await f.Receipts.Confirm(equalReceipt.GoodsReceiptId, default);
        var equalLot = Assert.Single(equalReceipt.GoodsReceiptItems);
        f.Db.StockMovements.Add(new StockMovement
        {
            StockMovementId = Guid.NewGuid(),
            IngredientId = equalThreshold.IngredientId,
            GoodsReceiptItemId = equalLot.GoodsReceiptItemId,
            MovementType = "Issue",
            QuantityChange = -10,
            CreatedAt = clock.GetUtcNow().UtcDateTime
        });
        equalThreshold.StockQuantity = 15;
        await f.Db.SaveChangesAsync();

        // 4. Draft receipt only (not confirmed) -> 0 issuable <= 5 -> low stock.
        var draftOnly = await f.Ingredients.Create(new IngredientWriteDTO
        {
            IngredientName = "Draft Only",
            BaseUnit = "g",
            LowStockThreshold = 5
        }, default);
        await f.Receipts.Create(new ReceiptWriteDTO
        {
            GoodsReceiptId = Guid.NewGuid(),
            Note = "Draft lot",
            Items =
            [
                new ReceiptLineDTO
                {
                    IngredientId = draftOnly.IngredientId,
                    ReceivedQuantity = 50,
                    ExpiryDate = today.AddDays(10)
                }
            ]
        }, default);

        // 5. Healthy active item expiring today (today >= today is issuable: 15 > 10) -> NOT low stock.
        var healthyToday = await f.Ingredients.Create(new IngredientWriteDTO
        {
            IngredientName = "Healthy Today",
            BaseUnit = "g",
            LowStockThreshold = 10
        }, default);
        var todayReceipt = await f.Receipts.Create(new ReceiptWriteDTO
        {
            GoodsReceiptId = Guid.NewGuid(),
            Note = "Expires today",
            Items =
            [
                new ReceiptLineDTO
                {
                    IngredientId = healthyToday.IngredientId,
                    ReceivedQuantity = 15,
                    ExpiryDate = today
                }
            ]
        }, default);
        await f.Receipts.Confirm(todayReceipt.GoodsReceiptId, default);

        // 6. Inactive item with 0 stock and threshold 20 -> NOT low stock and excluded from lowStockOnly.
        var inactive = await f.Ingredients.Create(new IngredientWriteDTO
        {
            IngredientName = "Inactive Item",
            BaseUnit = "g",
            LowStockThreshold = 20
        }, default);
        await f.Ingredients.SetStatus(inactive.IngredientId, false, default);

        var allPage = await f.Ingredients.GetAll(1, 20, null, null, false, default);
        Assert.Equal(6, allPage.TotalCount);
        var byId = allPage.Items.ToDictionary(x => x.IngredientId);

        Assert.Equal(30m, byId[pepsi.IngredientId].StockQuantity);
        Assert.True(byId[pepsi.IngredientId].IsLowStock);

        Assert.Equal(10m, byId[allExpired.IngredientId].StockQuantity);
        Assert.True(byId[allExpired.IngredientId].IsLowStock);

        Assert.Equal(15m, byId[equalThreshold.IngredientId].StockQuantity);
        Assert.True(byId[equalThreshold.IngredientId].IsLowStock);

        Assert.Equal(0m, byId[draftOnly.IngredientId].StockQuantity);
        Assert.True(byId[draftOnly.IngredientId].IsLowStock);

        Assert.Equal(15m, byId[healthyToday.IngredientId].StockQuantity);
        Assert.False(byId[healthyToday.IngredientId].IsLowStock);

        Assert.Equal(0m, byId[inactive.IngredientId].StockQuantity);
        Assert.False(byId[inactive.IngredientId].IsLowStock);

        // Verify lowStockOnly filtering, totalCount after filter before pagination, and paging.
        var lowPage1 = await f.Ingredients.GetAll(1, 2, null, null, true, default);
        var lowPage2 = await f.Ingredients.GetAll(2, 2, null, null, true, default);
        Assert.Equal(4, lowPage1.TotalCount);
        Assert.Equal(4, lowPage2.TotalCount);
        Assert.Equal(2, lowPage1.Items.Count);
        Assert.Equal(2, lowPage2.Items.Count);
        Assert.All(lowPage1.Items.Concat(lowPage2.Items), item =>
        {
            Assert.True(item.IsActive);
            Assert.True(item.IsLowStock);
        });
        var lowIds = lowPage1.Items.Concat(lowPage2.Items).Select(x => x.IngredientId).ToHashSet();
        Assert.Equal(
            [pepsi.IngredientId, allExpired.IngredientId, equalThreshold.IngredientId, draftOnly.IngredientId],
            lowIds);

        Assert.True(await f.Ingredients.IsLowStockAsync(pepsi, default));
        Assert.False(await f.Ingredients.IsLowStockAsync(healthyToday, default));
        Assert.False(await f.Ingredients.IsLowStockAsync(inactive, default));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("0.0000001")]
    [InlineData("1000000000000")]
    public async Task InvalidReceiptQuantitiesAreRejected(string quantity)
    {
        using var f = new Fixture();
        var request = Receipt(Guid.NewGuid());
        request.Items[0].ReceivedQuantity = decimal.Parse(quantity, System.Globalization.CultureInfo.InvariantCulture);
        await Error(400, () => f.Receipts.Create(request, default));
        Assert.Empty(await f.Db.GoodsReceipts.ToListAsync());
    }

    [Fact]
    public async Task MissingExpiryDuplicateLinesAndMissingIngredientAreRejected()
    {
        using var f = new Fixture();
        var request = Receipt(Guid.NewGuid());
        request.Items[0].ExpiryDate = null;
        await Error(400, () => f.Receipts.Create(request, default));
        request.Items[0].ExpiryDate = new DateOnly(2030, 1, 1);
        request.Items.Add(request.Items[0]);
        await Error(400, () => f.Receipts.Create(request, default));
        request.Items.RemoveAt(1);
        await Error(404, () => f.Receipts.Create(request, default));
        await Error(400, () => f.Ingredients.GetAll(int.MaxValue, 100, null, null, false, default));
    }

    [Fact]
    public async Task StockOverflowDoesNotConfirmReceipt()
    {
        using var f = new Fixture();
        var ingredient = await f.AddIngredient();
        ingredient.StockQuantity = 999999999999m;
        await f.Db.SaveChangesAsync();
        var request = Receipt(ingredient.IngredientId);
        await f.Receipts.Create(request, default);
        await Error(409, () => f.Receipts.Confirm(request.GoodsReceiptId, default));
        f.Db.ChangeTracker.Clear();
        Assert.Equal("Draft", (await f.Receipts.GetById(request.GoodsReceiptId, default)).Status);
        Assert.Empty(await f.Db.StockMovements.ToListAsync());
    }

    [Fact]
    public async Task EveryOperationChecksAccess()
    {
        using var f = new Fixture();
        f.Access.Denied = true;
        await Error(403, () => f.AddIngredient());
        await Error(403, () => f.Ingredients.GetAll(1, 20, null, null, false, default));
        await Error(403, () => f.Ingredients.GetById(Guid.NewGuid(), default));
        await Error(403, () => f.Ingredients.Update(Guid.NewGuid(), new(), default));
        await Error(403, () => f.Ingredients.SetStatus(Guid.NewGuid(), true, default));
        await Error(403, () => f.Receipts.Create(Receipt(Guid.NewGuid()), default));
        await Error(403, () => f.Receipts.Confirm(Guid.NewGuid(), default));
        await Error(403, () => f.Receipts.GetAll(1, 20, default));
        await Error(403, () => f.Receipts.GetById(Guid.NewGuid(), default));
        await Error(403, () => f.Movements.GetAll(Guid.NewGuid(), 1, 20, default));
        Assert.Empty(await f.Db.Ingredients.ToListAsync());
    }

    private static async Task Error(int code, Func<Task> action)
    {
        Assert.Equal(code, (await Assert.ThrowsAsync<InventoryException>(action)).StatusCode);
    }

    private static ReceiptWriteDTO Receipt(Guid ingredientId)
    {
        return new ReceiptWriteDTO
        {
            GoodsReceiptId = Guid.NewGuid(),
            Note = "Receive rice",
            Items =
            [
                new ReceiptLineDTO
                {
                    IngredientId = ingredientId,
                    ReceivedQuantity = 12.5m,
                    ExpiryDate = new DateOnly(2030, 1, 1)
                }
            ]
        };
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
        public InventoryDbContext Db { get; }
        public Access Access { get; } = new();
        public IngredientService Ingredients { get; }
        public IngredientUnitService Conversions { get; }
        public GoodsReceiptService Receipts { get; }
        public StockMovementService Movements { get; }

        public Fixture(TimeProvider? clock = null)
        {
            var time = clock ?? TimeProvider.System;
            connection.Open();
            Db = new TestDb(new DbContextOptionsBuilder<InventoryDbContext>().UseSqlite(connection).Options);
            Db.Database.EnsureCreated();
            Db.Units.Add(new Unit
            {
                UnitId = Guid.NewGuid(),
                Code = "g",
                Name = "Gram"
            });
            Db.SaveChanges();
            var transaction = new InventoryTransaction(new UnitOfWork<InventoryDbContext>(Db));
            var ingredients = new IngredientRepository(Db);
            var movements = new StockMovementRepository(Db);
            var units = new UnitRepository(Db);
            Conversions = new IngredientUnitService(ingredients, units, new IngredientUnitRepository(Db), transaction, Access);
            Ingredients = new IngredientService(ingredients, transaction, Access, units, time);
            Receipts = new GoodsReceiptService(new GoodsReceiptRepository(Db), ingredients, movements, transaction, Access, time, Conversions);
            Movements = new StockMovementService(movements, ingredients, Access);
        }

        public Task<Ingredient> AddIngredient()
        {
            return Ingredients.Create(new IngredientWriteDTO
            {
                IngredientName = "Rice",
                BaseUnit = "g",
                LowStockThreshold = 10
            }, default);
        }

        public void Dispose()
        {
            Db.Dispose();
            connection.Dispose();
        }
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow()
        {
            return now.ToUniversalTime();
        }
    }
}
