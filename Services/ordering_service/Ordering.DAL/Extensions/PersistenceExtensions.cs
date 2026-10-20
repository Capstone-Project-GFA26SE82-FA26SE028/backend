using Ordering.DAL.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shared.Persistence.Interfaces;
using Shared.Persistence.Repositories;
using Shared.Persistence.UnitOfWorks;

namespace Ordering.DAL.Extensions;

public static class PersistenceExtensions
{
    public static IServiceCollection AddOrderingPersistence(this IServiceCollection services, string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        services.AddDbContext<OrderingDbContext>(options =>
        {
            options.UseSqlServer(connectionString);
        });
        services.AddScoped<IUnitOfWork, UnitOfWork<OrderingDbContext>>();
        services.AddScoped<Ordering.DAL.Interfaces.ICartRepository, Ordering.DAL.Implements.CartRepository>();
        services.AddScoped<Ordering.DAL.Interfaces.IOrderRepository, Ordering.DAL.Implements.OrderRepository>();
        services.AddScoped<Ordering.DAL.Interfaces.IKitchenTaskRepository, Ordering.DAL.Implements.KitchenTaskRepository>();
        services.AddScoped<Ordering.DAL.Interfaces.IOrderingTransaction, Ordering.DAL.Implements.OrderingTransaction>();
        services.AddScoped<IGenericRepository<global::Ordering.DAL.Models.CancellationRequest>, GenericRepository<global::Ordering.DAL.Models.CancellationRequest, OrderingDbContext>>();
        services.AddScoped<IGenericRepository<global::Ordering.DAL.Models.Cart>, GenericRepository<global::Ordering.DAL.Models.Cart, OrderingDbContext>>();
        services.AddScoped<IGenericRepository<global::Ordering.DAL.Models.CartItem>, GenericRepository<global::Ordering.DAL.Models.CartItem, OrderingDbContext>>();
        services.AddScoped<IGenericRepository<global::Ordering.DAL.Models.CartItemOption>, GenericRepository<global::Ordering.DAL.Models.CartItemOption, OrderingDbContext>>();
        services.AddScoped<IGenericRepository<global::Ordering.DAL.Models.Event>, GenericRepository<global::Ordering.DAL.Models.Event, OrderingDbContext>>();
        services.AddScoped<IGenericRepository<global::Ordering.DAL.Models.KitchenAssignment>, GenericRepository<global::Ordering.DAL.Models.KitchenAssignment, OrderingDbContext>>();
        services.AddScoped<IGenericRepository<global::Ordering.DAL.Models.KitchenStation>, GenericRepository<global::Ordering.DAL.Models.KitchenStation, OrderingDbContext>>();
        services.AddScoped<IGenericRepository<global::Ordering.DAL.Models.KitchenTask>, GenericRepository<global::Ordering.DAL.Models.KitchenTask, OrderingDbContext>>();
        services.AddScoped<IGenericRepository<global::Ordering.DAL.Models.Order>, GenericRepository<global::Ordering.DAL.Models.Order, OrderingDbContext>>();
        services.AddScoped<IGenericRepository<global::Ordering.DAL.Models.OrderItem>, GenericRepository<global::Ordering.DAL.Models.OrderItem, OrderingDbContext>>();
        services.AddScoped<IGenericRepository<global::Ordering.DAL.Models.OrderItemOption>, GenericRepository<global::Ordering.DAL.Models.OrderItemOption, OrderingDbContext>>();
        services.AddScoped<IGenericRepository<global::Ordering.DAL.Models.ServiceRecord>, GenericRepository<global::Ordering.DAL.Models.ServiceRecord, OrderingDbContext>>();

        return services;
    }
}
