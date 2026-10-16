using Billing.DAL.Extensions;
using Billing.DAL.Models;
using Catalog.DAL.Extensions;
using Catalog.DAL.Models;
using Customer.DAL.Extensions;
using Customer.DAL.Models;
using Inventory.DAL.Extensions;
using Inventory.DAL.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ordering.DAL.Extensions;
using Ordering.DAL.Models;
using Restaurant.DAL.Extensions;
using Restaurant.DAL.Models;
using Shared.Persistence.Interfaces;
using Shared.Persistence.Repositories;
using Shared.Persistence.UnitOfWorks;
using Xunit;

namespace Foundation.Tests;

public class PersistenceTests
{
    [Theory]
    [InlineData("Restaurant", typeof(RestaurantDbContext))]
    [InlineData("Catalog", typeof(CatalogDbContext))]
    [InlineData("Ordering", typeof(OrderingDbContext))]
    [InlineData("Customer", typeof(CustomerDbContext))]
    [InlineData("Inventory", typeof(InventoryDbContext))]
    [InlineData("Billing", typeof(BillingDbContext))]
    public void EveryModelHasAScopedRepository(string service, Type contextType)
    {
        var services = new ServiceCollection();
        const string connection = "Server=localhost;Database=ConfigurationTest;Integrated Security=True;TrustServerCertificate=True";
        switch (service)
        {
            case "Restaurant":
                services.AddRestaurantPersistence(connection);
                break;
            case "Catalog":
                services.AddCatalogPersistence(connection);
                break;
            case "Ordering":
                services.AddOrderingPersistence(connection);
                break;
            case "Customer":
                services.AddCustomerPersistence(connection);
                break;
            case "Inventory":
                services.AddInventoryPersistence(connection);
                break;
            case "Billing":
                services.AddBillingPersistence(connection);
                break;
        }

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true
        });
        using var scope = provider.CreateScope();
        using var secondScope = provider.CreateScope();
        var db = (DbContext)scope.ServiceProvider.GetRequiredService(contextType);
        Assert.Same(db, scope.ServiceProvider.GetRequiredService(contextType));
        Assert.NotSame(db, secondScope.ServiceProvider.GetRequiredService(contextType));
        Assert.IsAssignableFrom<IUnitOfWork>(scope.ServiceProvider.GetRequiredService<IUnitOfWork>());
        if (service == "Restaurant")
        {
            Assert.Same(scope.ServiceProvider.GetRequiredService<Restaurant.DAL.Interfaces.IAreaRepository>(),
                scope.ServiceProvider.GetRequiredService<IGenericRepository<Area>>());
            Assert.Same(scope.ServiceProvider.GetRequiredService<Restaurant.DAL.Interfaces.ITableRepository>(),
                scope.ServiceProvider.GetRequiredService<IGenericRepository<Table>>());
            Assert.Same(scope.ServiceProvider.GetRequiredService<Restaurant.DAL.Interfaces.IQrRepository>(),
                scope.ServiceProvider.GetRequiredService<IGenericRepository<Qr>>());
            Assert.Same(scope.ServiceProvider.GetRequiredService<Restaurant.DAL.Interfaces.IRestaurantRepository>(),
                scope.ServiceProvider.GetRequiredService<IGenericRepository<Restaurant.DAL.Models.Restaurant>>());
        }

        // Implicit many-to-many join tables are managed through navigation properties.
        foreach (var entity in db.Model.GetEntityTypes().Where(entity => !entity.HasSharedClrType))
        {
            var repositoryType = typeof(IGenericRepository<>).MakeGenericType(entity.ClrType);
            var repository = scope.ServiceProvider.GetRequiredService(repositoryType);
            Assert.Same(repository, scope.ServiceProvider.GetRequiredService(repositoryType));
            Assert.NotSame(repository, secondScope.ServiceProvider.GetRequiredService(repositoryType));
        }
    }

    [Fact]
    public async Task DeleteWaitsForUnitOfWorkSave()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = CreateContext(connection);
        await db.Database.EnsureCreatedAsync();
        var repository = new GenericRepository<TestEntity, TestContext>(db);
        using var unitOfWork = new UnitOfWork<TestContext>(db);
        var entity = new TestEntity
        {
            Id = Guid.NewGuid(),
            Name = "Initial"
        };
        await repository.AddAsync(entity);
        await unitOfWork.SaveAsync();

        await repository.DeleteAsync(entity.Id);
        Assert.Equal(1, await db.Entities.AsNoTracking().CountAsync());
        await unitOfWork.SaveAsync();
        Assert.Equal(0, await db.Entities.CountAsync());
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task TransactionCommitsOrRollsBackAllWrites(bool fail, bool explicitIsolation)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = CreateContext(connection);
        await db.Database.EnsureCreatedAsync();
        using var unitOfWork = new UnitOfWork<TestContext>(db);
        Func<Task<bool>> action = async () =>
        {
            db.Entities.Add(new TestEntity
            {
                Id = Guid.NewGuid(),
                Name = "First"
            });
            await unitOfWork.SaveAsync();
            if (fail)
            {
                throw new InvalidOperationException("Simulated business failure");
            }

            db.Entities.Add(new TestEntity
            {
                Id = Guid.NewGuid(),
                Name = "Second"
            });
            return true;
        };
        Func<Task<bool>> operation = () => explicitIsolation
            ? unitOfWork.ExecuteInTransactionAsync(action, System.Data.IsolationLevel.Serializable)
            : unitOfWork.ExecuteInTransactionAsync(action);

        if (fail)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(operation);
        }
        else
        {
            Assert.True(await operation());
        }

        await using var verification = CreateContext(connection);
        Assert.Equal(fail ? 0 : 2, await verification.Entities.CountAsync());
    }

    [Fact]
    public async Task AsyncFilterReturnsOnlyMatchingRows()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = CreateContext(connection);
        await db.Database.EnsureCreatedAsync();
        db.Entities.AddRange(new TestEntity
        {
            Id = Guid.NewGuid(),
            Name = "Match"
        }, new TestEntity
        {
            Id = Guid.NewGuid(),
            Name = "Other"
        });
        await db.SaveChangesAsync();
        var repository = new GenericRepository<TestEntity, TestContext>(db);
        var result = await repository.FindAllAsync(entity => entity.Name == "Match");
        Assert.Equal("Match", Assert.Single(result).Name);
    }

    private static TestContext CreateContext(SqliteConnection connection)
    {
        return new TestContext(new DbContextOptionsBuilder<TestContext>().UseSqlite(connection).Options);
    }

    public class TestContext : DbContext
    {
        public TestContext(DbContextOptions<TestContext> options) : base(options)
        {
        }

        public DbSet<TestEntity> Entities { get; set; }
    }

    public class TestEntity
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public Guid? ParentId { get; set; }
        public TestEntity? Parent { get; set; }
    }

    [Fact]
    public async Task IncludeLoadsRelatedEntityAndKeepsFilter()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = CreateContext(connection);
        await db.Database.EnsureCreatedAsync();
        var parent = new TestEntity
        {
            Id = Guid.NewGuid(),
            Name = "Parent"
        };
        var child = new TestEntity
        {
            Id = Guid.NewGuid(),
            Name = "Child",
            Parent = parent
        };
        db.Entities.Add(child);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repository = new GenericRepository<TestEntity, TestContext>(db);
        var result = await repository.GetAllByListAsync(entity => entity.Id == child.Id,
            query => query.Include(entity => entity.Parent));

        Assert.Equal("Parent", Assert.Single(result).Parent!.Name);
    }
}
