using Inventory.DAL.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shared.Persistence.Interfaces;
using Shared.Persistence.Repositories;
using Shared.Persistence.UnitOfWorks;

namespace Inventory.DAL.Extensions;

public static class PersistenceExtensions
{
    public static IServiceCollection AddInventoryPersistence(this IServiceCollection services, string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        services.AddDbContext<InventoryDbContext>(options =>
        {
            options.UseSqlServer(connectionString);
        });
        services.AddScoped<IUnitOfWork, UnitOfWork<InventoryDbContext>>();
        services.AddScoped<IGenericRepository<StockIssue>, GenericRepository<StockIssue, InventoryDbContext>>();
        services.AddScoped<Repositories.StockIssueRepository>();
        services.AddScoped<Repositories.StockLotRepository>();
        services.AddScoped<IGenericRepository<Unit>, GenericRepository<Unit, InventoryDbContext>>();
        services.AddScoped<IGenericRepository<IngredientUnit>, GenericRepository<IngredientUnit, InventoryDbContext>>();
        services.AddScoped<InventoryTransaction>();
        services.AddScoped<Repositories.UnitRepository>();
        services.AddScoped<Repositories.IngredientUnitRepository>();
        services.AddScoped<Repositories.IngredientRepository>();
        services.AddScoped<Repositories.GoodsReceiptRepository>();
        services.AddScoped<Repositories.StockMovementRepository>();
        services.AddScoped<IGenericRepository<global::Inventory.DAL.Models.GoodsReceipt>, GenericRepository<global::Inventory.DAL.Models.GoodsReceipt, InventoryDbContext>>();
        services.AddScoped<IGenericRepository<global::Inventory.DAL.Models.GoodsReceiptItem>, GenericRepository<global::Inventory.DAL.Models.GoodsReceiptItem, InventoryDbContext>>();
        services.AddScoped<IGenericRepository<global::Inventory.DAL.Models.Ingredient>, GenericRepository<global::Inventory.DAL.Models.Ingredient, InventoryDbContext>>();
        services.AddScoped<IGenericRepository<global::Inventory.DAL.Models.StockMovement>, GenericRepository<global::Inventory.DAL.Models.StockMovement, InventoryDbContext>>();
        services.AddScoped<IGenericRepository<global::Inventory.DAL.Models.Stocktake>, GenericRepository<global::Inventory.DAL.Models.Stocktake, InventoryDbContext>>();
        services.AddScoped<IGenericRepository<global::Inventory.DAL.Models.StocktakeItem>, GenericRepository<global::Inventory.DAL.Models.StocktakeItem, InventoryDbContext>>();

        return services;
    }
}
