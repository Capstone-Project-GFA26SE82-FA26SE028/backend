using Catalog.DAL.Models;
using Inventory.DAL.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Shared.Persistence.RestaurantIsolation;
using Xunit;

namespace Foundation.Tests;

public sealed class RestaurantIsolationTests
{
    [Fact]
    public async Task InventoryReadsAndWritesAreIsolatedAndUnitCodesMayRepeat()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<InventoryDbContext>().UseSqlite(connection).Options;
        var restaurantA = Guid.NewGuid();
        var restaurantB = Guid.NewGuid();
        var unitA = Guid.NewGuid();
        await using (var db = new InventoryDbContext(options, Scope(restaurantA)))
        {
            await db.Database.EnsureCreatedAsync();
            db.Units.Add(new Unit
            {
                UnitId = unitA,
                Code = "kg",
                Name = "Kilogram"
            });
            await db.SaveChangesAsync();
        }

        await using (var db = new InventoryDbContext(options, Scope(restaurantB)))
        {
            Assert.Empty(await db.Units.ToListAsync());
            Assert.Null(await db.Units.FindAsync(unitA));
            db.Units.Add(new Unit
            {
                UnitId = Guid.NewGuid(),
                Code = "kg",
                Name = "Kilogram B"
            });
            await db.SaveChangesAsync();
            Assert.Single(await db.Units.ToListAsync());
        }

        await using (var db = new InventoryDbContext(options, Scope(restaurantB)))
        {
            var forged = new Unit
            {
                UnitId = unitA,
                Code = "forged",
                Name = "forged"
            };
            db.Attach(forged);
            db.Entry(forged).Property("RestaurantId").CurrentValue = restaurantB;
            db.Entry(forged).State = EntityState.Modified;
            await Assert.ThrowsAsync<RestaurantScopeException>(() => db.SaveChangesAsync());
        }

        await using (var db = new InventoryDbContext(options, Scope(restaurantA)))
        {
            Assert.Equal("kg", (await db.Units.SingleAsync()).Code);
        }
    }

    [Fact]
    public async Task UnselectedScopeHidesRowsAndRejectsWrites()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<CatalogDbContext>().UseSqlite(connection).Options;
        await using var db = new CatalogDbContext(options, new RestaurantDataScope());
        await db.Database.EnsureCreatedAsync();
        Assert.Empty(await db.Categories.ToListAsync());
        db.Categories.Add(Category());
        await Assert.ThrowsAsync<RestaurantScopeException>(() => db.SaveChangesAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CrossRestaurantForeignKeyIsRejectedEvenWhenParentIsTracked(bool forgeOwner)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<CatalogDbContext>().UseSqlite(connection).Options;
        var restaurantA = Guid.NewGuid();
        var category = Category();
        await using (var db = new CatalogDbContext(options, Scope(restaurantA)))
        {
            await db.Database.EnsureCreatedAsync();
            db.Categories.Add(category);
            await db.SaveChangesAsync();
        }

        await using var other = new CatalogDbContext(options, Scope(Guid.NewGuid()));
        await other.Categories.IgnoreQueryFilters().LoadAsync();
        if (forgeOwner)
        {
            other.Entry(other.Categories.Local.Single()).Property("RestaurantId").CurrentValue = other.CurrentRestaurantId;
            other.Entry(other.Categories.Local.Single()).State = EntityState.Unchanged;
        }
        other.Items.Add(new Item
        {
            ItemId = Guid.NewGuid(),
            CategoryId = category.CategoryId,
            ItemName = "Test",
            ItemCode = "TEST",
            Availability = "Inactive"
        });
        await Assert.ThrowsAsync<RestaurantScopeException>(() => other.SaveChangesAsync());
    }

    [Fact]
    public void RequestCannotSwitchRestaurantsAfterSelection()
    {
        var scope = Scope(Guid.NewGuid());
        Assert.Throws<RestaurantScopeException>(() => scope.Select(Guid.NewGuid()));
    }

    private static RestaurantDataScope Scope(Guid id)
    {
        var scope = new RestaurantDataScope();
        scope.Select(id);
        return scope;
    }

    private static Category Category()
    {
        return new Category
        {
            CategoryId = Guid.NewGuid(),
            CategoryCode = "FOOD",
            CategoryName = "Food",
            IsActive = true
        };
    }
}
