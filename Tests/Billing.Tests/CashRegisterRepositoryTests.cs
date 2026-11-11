using Billing.Common.DTOs;
using Billing.DAL.Implements;
using Billing.DAL.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Shared.Persistence.UnitOfWorks;

namespace Billing.Tests;

public sealed class CashRegisterRepositoryTests
{
    [Fact]
    public async Task ScopedPagingSearchAndRestaurantFilterExecuteAgainstTheDatabase()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = await CreateContext(connection);
        var firstRestaurant = Guid.NewGuid();
        var secondRestaurant = Guid.NewGuid();
        db.CashRegisters.AddRange(
            Register(firstRestaurant, "POS-A"),
            Register(firstRestaurant, "POS-B"),
            Register(firstRestaurant, "OTHER"),
            Register(secondRestaurant, "POS-C"));
        await db.SaveChangesAsync();
        var repository = new CashRegisterRepository(db);

        var result = await repository.GetPageAsync(new CashRegisterQueryDTO
        {
            Search = "pos",
            Page = 2,
            PageSize = 1
        }, [firstRestaurant], false, default);

        Assert.Equal(2, result.TotalCount);
        Assert.Equal("POS-B", Assert.Single(result.Items).Code);
        var empty = await repository.GetPageAsync(new CashRegisterQueryDTO
        {
            RestaurantId = secondRestaurant
        }, [firstRestaurant], false, default);
        Assert.Equal(0, empty.TotalCount);
        var all = await repository.GetPageAsync(new CashRegisterQueryDTO(), null, false, default);
        Assert.Equal(4, all.TotalCount);
    }

    [Fact]
    public async Task DuplicateDetectionIsCaseInsensitiveAndBranchSpecific()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = await CreateContext(connection);
        var restaurantId = Guid.NewGuid();
        db.CashRegisters.Add(Register(restaurantId, "POS-A"));
        await db.SaveChangesAsync();
        var repository = new CashRegisterRepository(db);

        Assert.True(await repository.CodeExistsAsync(restaurantId, "pos-a", default));
        Assert.False(await repository.CodeExistsAsync(Guid.NewGuid(), "POS-A", default));
    }

    [Fact]
    public async Task CreationLockRequiresTransactionAndUnitOfWorkSavesTheBranchReference()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = await CreateContext(connection);
        var restaurantId = Guid.NewGuid();
        var repository = new CashRegisterRepository(db);
        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.LockRestaurantAsync(restaurantId, default));

        using var unitOfWork = new UnitOfWork<BillingDbContext>(db);
        var register = await unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            Assert.True(await repository.LockRestaurantAsync(restaurantId, default));
            return await repository.AddAsync(Register(restaurantId, "POS-A"));
        });

        db.ChangeTracker.Clear();
        var stored = await repository.GetByIdAsync(register.CashRegisterId, default);
        Assert.NotNull(stored);
        Assert.Equal(restaurantId, stored.RestaurantId);
    }

    private static async Task<BillingDbContext> CreateContext(SqliteConnection connection)
    {
        var db = new BillingDbContext(new DbContextOptionsBuilder<BillingDbContext>().UseSqlite(connection).Options);
        await db.Database.ExecuteSqlRawAsync("CREATE TABLE CashRegister (CashRegisterId TEXT NOT NULL PRIMARY KEY, RestaurantId TEXT NOT NULL, Code TEXT NOT NULL);");
        return db;
    }

    private static CashRegister Register(Guid restaurantId, string code)
    {
        return new CashRegister
        {
            CashRegisterId = Guid.NewGuid(),
            RestaurantId = restaurantId,
            Code = code
        };
    }
}
