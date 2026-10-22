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
        var page = await f.Ingredients.GetAll(1, 20, "renamed", false, true, default);
        Assert.Equal("Rice renamed", Assert.Single(page.Items).IngredientName);
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

        public Fixture()
        {
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
            Ingredients = new IngredientService(ingredients, transaction, Access, units);
            Receipts = new GoodsReceiptService(new GoodsReceiptRepository(Db), ingredients, movements, transaction, Access, TimeProvider.System, Conversions);
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
}
