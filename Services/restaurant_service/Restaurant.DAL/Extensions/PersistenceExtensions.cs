using Restaurant.DAL.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shared.Persistence.Interfaces;
using Shared.Persistence.Repositories;
using Shared.Persistence.UnitOfWorks;

namespace Restaurant.DAL.Extensions;

public static class PersistenceExtensions
{
    public static IServiceCollection AddRestaurantPersistence(this IServiceCollection services, string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        services.AddDbContext<RestaurantDbContext>(options =>
        {
            options.UseSqlServer(connectionString);
        });
        services.AddScoped<IUnitOfWork, UnitOfWork<RestaurantDbContext>>();
        services.AddScoped<IGenericRepository<global::Restaurant.DAL.Models.Area>, GenericRepository<global::Restaurant.DAL.Models.Area, RestaurantDbContext>>();
        services.AddScoped<IGenericRepository<global::Restaurant.DAL.Models.GuestAccess>, GenericRepository<global::Restaurant.DAL.Models.GuestAccess, RestaurantDbContext>>();
        services.AddScoped<IGenericRepository<global::Restaurant.DAL.Models.OpeningHour>, GenericRepository<global::Restaurant.DAL.Models.OpeningHour, RestaurantDbContext>>();
        services.AddScoped<IGenericRepository<global::Restaurant.DAL.Models.Qr>, GenericRepository<global::Restaurant.DAL.Models.Qr, RestaurantDbContext>>();
        services.AddScoped<IGenericRepository<global::Restaurant.DAL.Models.Reservation>, GenericRepository<global::Restaurant.DAL.Models.Reservation, RestaurantDbContext>>();
        services.AddScoped<IGenericRepository<global::Restaurant.DAL.Models.Restaurant>, GenericRepository<global::Restaurant.DAL.Models.Restaurant, RestaurantDbContext>>();
        services.AddScoped<IGenericRepository<global::Restaurant.DAL.Models.ServiceRequest>, GenericRepository<global::Restaurant.DAL.Models.ServiceRequest, RestaurantDbContext>>();
        services.AddScoped<IGenericRepository<global::Restaurant.DAL.Models.SessionTable>, GenericRepository<global::Restaurant.DAL.Models.SessionTable, RestaurantDbContext>>();
        services.AddScoped<IGenericRepository<global::Restaurant.DAL.Models.Table>, GenericRepository<global::Restaurant.DAL.Models.Table, RestaurantDbContext>>();
        services.AddScoped<IGenericRepository<global::Restaurant.DAL.Models.TableSession>, GenericRepository<global::Restaurant.DAL.Models.TableSession, RestaurantDbContext>>();

        return services;
    }
}
