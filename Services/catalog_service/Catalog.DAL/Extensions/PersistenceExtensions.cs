using Catalog.DAL.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shared.Persistence.Interfaces;
using Shared.Persistence.Repositories;
using Shared.Persistence.UnitOfWorks;

namespace Catalog.DAL.Extensions;

public static class PersistenceExtensions
{
    public static IServiceCollection AddCatalogPersistence(this IServiceCollection services, string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        services.AddDbContext<CatalogDbContext>(options =>
        {
            options.UseSqlServer(connectionString);
        });
        services.AddScoped<IUnitOfWork, UnitOfWork<CatalogDbContext>>();
        services.AddScoped<IGenericRepository<global::Catalog.DAL.Models.Category>, GenericRepository<global::Catalog.DAL.Models.Category, CatalogDbContext>>();
        services.AddScoped<IGenericRepository<global::Catalog.DAL.Models.Item>, GenericRepository<global::Catalog.DAL.Models.Item, CatalogDbContext>>();
        services.AddScoped<IGenericRepository<global::Catalog.DAL.Models.ItemOptionGroup>, GenericRepository<global::Catalog.DAL.Models.ItemOptionGroup, CatalogDbContext>>();
        services.AddScoped<IGenericRepository<global::Catalog.DAL.Models.Option>, GenericRepository<global::Catalog.DAL.Models.Option, CatalogDbContext>>();
        services.AddScoped<IGenericRepository<global::Catalog.DAL.Models.OptionGroup>, GenericRepository<global::Catalog.DAL.Models.OptionGroup, CatalogDbContext>>();
        services.AddScoped<IGenericRepository<global::Catalog.DAL.Models.Price>, GenericRepository<global::Catalog.DAL.Models.Price, CatalogDbContext>>();
        services.AddScoped<IGenericRepository<global::Catalog.DAL.Models.RecipeIngredient>, GenericRepository<global::Catalog.DAL.Models.RecipeIngredient, CatalogDbContext>>();
        services.AddScoped<IGenericRepository<global::Catalog.DAL.Models.RecipeMedium>, GenericRepository<global::Catalog.DAL.Models.RecipeMedium, CatalogDbContext>>();
        services.AddScoped<IGenericRepository<global::Catalog.DAL.Models.RecipeVersion>, GenericRepository<global::Catalog.DAL.Models.RecipeVersion, CatalogDbContext>>();

        return services;
    }
}
