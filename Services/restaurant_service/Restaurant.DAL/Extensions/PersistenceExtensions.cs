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
        services.AddScoped<Interfaces.IRestaurantRepository, Implements.RestaurantRepository>();
        services.AddScoped<Interfaces.IAreaRepository, Implements.AreaRepository>();
        services.AddScoped<Interfaces.ITableRepository, Implements.TableRepository>();
        services.AddScoped<Interfaces.IQrRepository, Implements.QrRepository>();
        services.AddScoped<Interfaces.IManagementTransaction, Implements.ManagementTransaction>();
        services.AddScoped<Interfaces.IReservationTransaction, Implements.ReservationTransaction>();
        services.AddScoped<Interfaces.ITableSessionRepository, Implements.TableSessionRepository>();
        services.AddScoped<Interfaces.IReservationRepository, Implements.ReservationRepository>();
        services.AddScoped<Interfaces.IRestaurantStaffRepository, Implements.RestaurantStaffRepository>();
        services.AddScoped<IGenericRepository<RestaurantStaff>>(provider => provider.GetRequiredService<Interfaces.IRestaurantStaffRepository>());
        services.AddScoped<Interfaces.ISessionTableRepository, Implements.SessionTableRepository>();
        services.AddScoped<IGenericRepository<global::Restaurant.DAL.Models.Area>>(provider => provider.GetRequiredService<Interfaces.IAreaRepository>());
        services.AddScoped<Interfaces.IGuestAccessRepository, Implements.GuestAccessRepository>();
        services.AddScoped<IGenericRepository<global::Restaurant.DAL.Models.GuestAccess>>(provider => provider.GetRequiredService<Interfaces.IGuestAccessRepository>());
        services.AddScoped<IGenericRepository<global::Restaurant.DAL.Models.OpeningHour>, GenericRepository<global::Restaurant.DAL.Models.OpeningHour, RestaurantDbContext>>();
        services.AddScoped<IGenericRepository<global::Restaurant.DAL.Models.Qr>>(provider => provider.GetRequiredService<Interfaces.IQrRepository>());
        services.AddScoped<IGenericRepository<global::Restaurant.DAL.Models.Reservation>, GenericRepository<global::Restaurant.DAL.Models.Reservation, RestaurantDbContext>>();
        services.AddScoped<IGenericRepository<global::Restaurant.DAL.Models.Restaurant>>(provider => provider.GetRequiredService<Interfaces.IRestaurantRepository>());
        services.AddScoped<IGenericRepository<global::Restaurant.DAL.Models.ServiceRequest>, GenericRepository<global::Restaurant.DAL.Models.ServiceRequest, RestaurantDbContext>>();
        services.AddScoped<IGenericRepository<global::Restaurant.DAL.Models.SessionTable>>(provider => provider.GetRequiredService<Interfaces.ISessionTableRepository>());
        services.AddScoped<IGenericRepository<global::Restaurant.DAL.Models.Table>>(provider => provider.GetRequiredService<Interfaces.ITableRepository>());
        services.AddScoped<IGenericRepository<global::Restaurant.DAL.Models.TableSession>>(provider => provider.GetRequiredService<Interfaces.ITableSessionRepository>());

        return services;
    }
}
