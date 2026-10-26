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

public sealed class CookingDeductionTests
{
    [Fact]
    public async Task DeductForCooking_SingleDish_AllocatesLotsFifoAndDeductsStock()
    {
        using var f = new Fixture();
        var beef = await f.Ingredient("Beef", "g", lowStockThreshold: 100);
        var lot1 = await f.Lot(beef, 200, new DateOnly(2026, 12, 1), -2);
        var lot2 = await f.Lot(beef, 500, new DateOnly(2026, 12, 10), -1);

        var cookingEventId = Guid.NewGuid();
        var taskId = Guid.NewGuid();
        var orderItemId = Guid.NewGuid();

        var request = new CookingDeductionDTO
        {
            CookingEventId = cookingEventId,
            Items =
            [
                new CookingItemDeductionDTO
                {
                    TaskId = taskId,
                    OrderItemId = orderItemId,
                    DishName = "Phở Bò",
                    Quantity = 2,
                    Ingredients =
                    [
                        new StockIssueLineDTO
                        {
                            IngredientId = beef.IngredientId,
                            Quantity = 350
                        }
                    ]
                }
            ]
        };

        var result = await f.Issues.DeductForCooking(request, default);

        Assert.Equal(cookingEventId, result.StockIssueId);
        Assert.Equal(2, result.Movements.Count);

        var firstAllocation = result.Movements.Single(m => m.GoodsReceiptItemId == lot1);
        Assert.Equal(-200, firstAllocation.QuantityChange);
        Assert.Equal("Cooking", firstAllocation.MovementType);
        Assert.Equal(cookingEventId, firstAllocation.CookingEventRef);
        Assert.Equal(orderItemId, firstAllocation.OrderItemRef);

        var secondAllocation = result.Movements.Single(m => m.GoodsReceiptItemId == lot2);
        Assert.Equal(-150, secondAllocation.QuantityChange);
        Assert.Equal("Cooking", secondAllocation.MovementType);
        Assert.Equal(cookingEventId, secondAllocation.CookingEventRef);
        Assert.Equal(orderItemId, secondAllocation.OrderItemRef);

        // Verify remaining stock is 700 - 350 = 350
        var updated = await f.Db.Ingredients.SingleAsync(x => x.IngredientId == beef.IngredientId);
        Assert.Equal(350, updated.StockQuantity);
        Assert.Empty(result.LowStockAlerts);
    }

    [Fact]
    public async Task DeductForCooking_SkipsExpiredLots()
    {
        using var f = new Fixture();
        var pork = await f.Ingredient("Pork", "g", lowStockThreshold: 50);
        await f.Lot(pork, 500, new DateOnly(2026, 10, 20), -3); // Expired (today is 2026-10-22)
        var validLot = await f.Lot(pork, 300, new DateOnly(2026, 11, 1), -1);

        var cookingEventId = Guid.NewGuid();
        var request = new CookingDeductionDTO
        {
            CookingEventId = cookingEventId,
            Items =
            [
                new CookingItemDeductionDTO
                {
                    TaskId = Guid.NewGuid(),
                    OrderItemId = Guid.NewGuid(),
                    DishName = "Cơm Sườn",
                    Quantity = 1,
                    Ingredients =
                    [
                        new StockIssueLineDTO
                        {
                            IngredientId = pork.IngredientId,
                            Quantity = 200
                        }
                    ]
                }
            ]
        };

        var result = await f.Issues.DeductForCooking(request, default);

        Assert.Single(result.Movements);
        Assert.Equal(validLot, result.Movements[0].GoodsReceiptItemId);
        Assert.Equal(-200, result.Movements[0].QuantityChange);

        var updated = await f.Db.Ingredients.SingleAsync(x => x.IngredientId == pork.IngredientId);
        Assert.Equal(600, updated.StockQuantity); // 800 - 200 = 600
    }

    [Fact]
    public async Task DeductForCooking_InsufficientUnexpiredStock_Throws409()
    {
        using var f = new Fixture();
        var chicken = await f.Ingredient("Chicken", "g");
        await f.Lot(chicken, 500, new DateOnly(2026, 10, 20), -2); // Expired
        await f.Lot(chicken, 100, new DateOnly(2026, 11, 1), -1); // Valid 100g

        var request = new CookingDeductionDTO
        {
            CookingEventId = Guid.NewGuid(),
            Items =
            [
                new CookingItemDeductionDTO
                {
                    TaskId = Guid.NewGuid(),
                    OrderItemId = Guid.NewGuid(),
                    DishName = "Gà Hấp",
                    Quantity = 1,
                    Ingredients =
                    [
                        new StockIssueLineDTO
                        {
                            IngredientId = chicken.IngredientId,
                            Quantity = 150 // Needs 150g, only 100g valid
                        }
                    ]
                }
            ]
        };

        var ex = await Assert.ThrowsAsync<InventoryException>(() => f.Issues.DeductForCooking(request, default));
        Assert.Equal(409, ex.StatusCode);
    }

    [Fact]
    public async Task DeductForCooking_Idempotent_SameCookingEventId_ReturnsExistingWithoutReDeducting()
    {
        using var f = new Fixture();
        var beef = await f.Ingredient("Beef", "g");
        await f.Lot(beef, 500, new DateOnly(2026, 12, 1), -1);

        var cookingEventId = Guid.NewGuid();
        var taskId = Guid.NewGuid();
        var orderItemId = Guid.NewGuid();

        var request = new CookingDeductionDTO
        {
            CookingEventId = cookingEventId,
            Items =
            [
                new CookingItemDeductionDTO
                {
                    TaskId = taskId,
                    OrderItemId = orderItemId,
                    DishName = "Bò Lúc Lắc",
                    Quantity = 1,
                    Ingredients =
                    [
                        new StockIssueLineDTO
                        {
                            IngredientId = beef.IngredientId,
                            Quantity = 200
                        }
                    ]
                }
            ]
        };

        var firstResult = await f.Issues.DeductForCooking(request, default);
        var initialMovements = await f.Db.StockMovements.Where(m => m.MovementType == "Cooking").ToListAsync();
        Assert.Equal(200, initialMovements.Sum(m => -m.QuantityChange));
        Assert.Equal(300, (await f.Db.Ingredients.SingleAsync(x => x.IngredientId == beef.IngredientId)).StockQuantity);

        // Call again with same event ID (retry scenario)
        f.Db.ChangeTracker.Clear();
        var secondResult = await f.Issues.DeductForCooking(request, default);

        Assert.Equal(firstResult.StockIssueId, secondResult.StockIssueId);
        // Ensure stock was NOT deducted again
        Assert.Equal(300, (await f.Db.Ingredients.SingleAsync(x => x.IngredientId == beef.IngredientId)).StockQuantity);
        var finalMovements = await f.Db.StockMovements.Where(m => m.MovementType == "Cooking").ToListAsync();
        Assert.Equal(200, finalMovements.Sum(m => -m.QuantityChange));
    }

    [Fact]
    public async Task DeductForCooking_LowStock_EmitsLowStockAlert()
    {
        using var f = new Fixture();
        var cheese = await f.Ingredient("Cheese", "g", lowStockThreshold: 100);
        await f.Lot(cheese, 150, new DateOnly(2026, 12, 1), -1);

        var request = new CookingDeductionDTO
        {
            CookingEventId = Guid.NewGuid(),
            Items =
            [
                new CookingItemDeductionDTO
                {
                    TaskId = Guid.NewGuid(),
                    OrderItemId = Guid.NewGuid(),
                    DishName = "Pizza Phô Mai",
                    Quantity = 1,
                    Ingredients =
                    [
                        new StockIssueLineDTO
                        {
                            IngredientId = cheese.IngredientId,
                            Quantity = 80 // 150 - 80 = 70 <= 100 (threshold)
                        }
                    ]
                }
            ]
        };

        var result = await f.Issues.DeductForCooking(request, default);

        var alert = Assert.Single(result.LowStockAlerts);
        Assert.Equal(cheese.IngredientId, alert.IngredientId);
        Assert.Equal("Cheese", alert.IngredientName);
        Assert.Equal(70, alert.CurrentStock);
        Assert.Equal(100, alert.Threshold);
    }

    [Fact]
    public async Task DeductForCooking_MultipleItemsInBatch_DeductsCorrectlyWithCorrectEventAndOrderItemRefs()
    {
        using var f = new Fixture();
        var rice = await f.Ingredient("Gạo ST25", "g");
        var egg = await f.Ingredient("Trứng Gà", "quả");
        await f.Lot(rice, 1000, new DateOnly(2026, 12, 1), -2);
        await f.Lot(egg, 50, new DateOnly(2026, 11, 1), -1);

        var cookingEventId = Guid.NewGuid();
        var task1Id = Guid.NewGuid();
        var orderItem1Id = Guid.NewGuid();
        var task2Id = Guid.NewGuid();
        var orderItem2Id = Guid.NewGuid();

        var request = new CookingDeductionDTO
        {
            CookingEventId = cookingEventId,
            Items =
            [
                new CookingItemDeductionDTO
                {
                    TaskId = task1Id,
                    OrderItemId = orderItem1Id,
                    DishName = "Cơm Chiên Trứng 1",
                    Quantity = 1,
                    Ingredients =
                    [
                        new StockIssueLineDTO { IngredientId = rice.IngredientId, Quantity = 200 },
                        new StockIssueLineDTO { IngredientId = egg.IngredientId, Quantity = 2 }
                    ]
                },
                new CookingItemDeductionDTO
                {
                    TaskId = task2Id,
                    OrderItemId = orderItem2Id,
                    DishName = "Cơm Chiên Trứng 2",
                    Quantity = 2,
                    Ingredients =
                    [
                        new StockIssueLineDTO { IngredientId = rice.IngredientId, Quantity = 400 },
                        new StockIssueLineDTO { IngredientId = egg.IngredientId, Quantity = 4 }
                    ]
                }
            ]
        };

        var result = await f.Issues.DeductForCooking(request, default);

        Assert.Equal(4, result.Movements.Count);

        var task1Movements = result.Movements.Where(m => m.OrderItemRef == orderItem1Id).ToList();
        Assert.Equal(2, task1Movements.Count);
        Assert.Contains(task1Movements, m => m.IngredientId == rice.IngredientId && m.QuantityChange == -200);
        Assert.Contains(task1Movements, m => m.IngredientId == egg.IngredientId && m.QuantityChange == -2);

        var task2Movements = result.Movements.Where(m => m.OrderItemRef == orderItem2Id).ToList();
        Assert.Equal(2, task2Movements.Count);
        Assert.Contains(task2Movements, m => m.IngredientId == rice.IngredientId && m.QuantityChange == -400);
        Assert.Contains(task2Movements, m => m.IngredientId == egg.IngredientId && m.QuantityChange == -4);

        var updatedRice = await f.Db.Ingredients.SingleAsync(x => x.IngredientId == rice.IngredientId);
        Assert.Equal(400, updatedRice.StockQuantity); // 1000 - 600 = 400

        var updatedEgg = await f.Db.Ingredients.SingleAsync(x => x.IngredientId == egg.IngredientId);
        Assert.Equal(44, updatedEgg.StockQuantity); // 50 - 6 = 44
    }

    [Fact]
    public async Task DeductForCooking_EmptyIngredients_ReturnsSuccessWithoutDeducting()
    {
        using var f = new Fixture();
        var request = new CookingDeductionDTO
        {
            CookingEventId = Guid.NewGuid(),
            Items =
            [
                new CookingItemDeductionDTO
                {
                    TaskId = Guid.NewGuid(),
                    OrderItemId = Guid.NewGuid(),
                    DishName = "Món Không Công Thức",
                    Quantity = 1,
                    Ingredients = []
                }
            ]
        };

        var result = await f.Issues.DeductForCooking(request, default);

        Assert.Empty(result.Movements);
        Assert.Empty(result.LowStockAlerts);
        Assert.Empty(await f.Db.StockIssues.ToListAsync());
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

        public Fixture()
        {
            connection.Open();
            Db = new TestDb(new DbContextOptionsBuilder<InventoryDbContext>().UseSqlite(connection).Options);
            Db.Database.EnsureCreated();
            var transaction = new InventoryTransaction(new UnitOfWork<InventoryDbContext>(Db));
            var ingredients = new IngredientRepository(Db);
            var lots = new StockLotRepository(Db);
            Issues = new StockIssueService(new StockIssueRepository(Db), lots, ingredients, transaction, Access, clock);
        }

        public async Task<Ingredient> Ingredient(string name = "Ingredient", string unit = "g", decimal lowStockThreshold = 0)
        {
            var ingredient = new Ingredient
            {
                IngredientId = Guid.NewGuid(),
                IngredientCode = Guid.NewGuid().ToString("N"),
                IngredientName = name,
                BaseUnit = unit,
                LowStockThreshold = lowStockThreshold,
                IsActive = true
            };
            Db.Ingredients.Add(ingredient);
            await Db.SaveChangesAsync();
            return ingredient;
        }

        public async Task<Guid> Lot(Ingredient ingredient, decimal quantity, DateOnly? expiry, int daysAgo)
        {
            var id = Guid.NewGuid();
            var time = clock.GetUtcNow().AddDays(daysAgo).UtcDateTime;
            var receipt = new GoodsReceipt
            {
                GoodsReceiptId = Guid.NewGuid(),
                ReceiptCode = Guid.NewGuid().ToString("N"),
                Status = "Confirmed",
                CreatedByRef = Access.Actor,
                CreatedAt = time,
                ConfirmAt = time,
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
            await Db.SaveChangesAsync();
            return id;
        }

        public void Dispose()
        {
            Db.Dispose();
            connection.Dispose();
        }
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

        public Task<Guid> RequireAsync(string operation, CancellationToken ct)
        {
            return Task.FromResult(Actor);
        }
    }
}
