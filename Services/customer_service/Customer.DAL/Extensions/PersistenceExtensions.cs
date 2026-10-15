using Customer.DAL.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shared.Persistence.Interfaces;
using Shared.Persistence.Repositories;
using Shared.Persistence.UnitOfWorks;

namespace Customer.DAL.Extensions;

public static class PersistenceExtensions
{
    public static IServiceCollection AddCustomerPersistence(this IServiceCollection services, string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        services.AddDbContext<CustomerDbContext>(options =>
        {
            options.UseSqlServer(connectionString);
        });
        services.AddScoped<IUnitOfWork, UnitOfWork<CustomerDbContext>>();
        services.AddScoped<IGenericRepository<global::Customer.DAL.Models.Customer>, GenericRepository<global::Customer.DAL.Models.Customer, CustomerDbContext>>();
        services.AddScoped<IGenericRepository<global::Customer.DAL.Models.Feedback>, GenericRepository<global::Customer.DAL.Models.Feedback, CustomerDbContext>>();
        services.AddScoped<IGenericRepository<global::Customer.DAL.Models.FeedbackAccess>, GenericRepository<global::Customer.DAL.Models.FeedbackAccess, CustomerDbContext>>();
        services.AddScoped<IGenericRepository<global::Customer.DAL.Models.FeedbackItem>, GenericRepository<global::Customer.DAL.Models.FeedbackItem, CustomerDbContext>>();
        services.AddScoped<IGenericRepository<global::Customer.DAL.Models.LoyaltyEntry>, GenericRepository<global::Customer.DAL.Models.LoyaltyEntry, CustomerDbContext>>();
        services.AddScoped<IGenericRepository<global::Customer.DAL.Models.LoyaltyPolicy>, GenericRepository<global::Customer.DAL.Models.LoyaltyPolicy, CustomerDbContext>>();
        services.AddScoped<IGenericRepository<global::Customer.DAL.Models.LoyaltyTier>, GenericRepository<global::Customer.DAL.Models.LoyaltyTier, CustomerDbContext>>();
        services.AddScoped<IGenericRepository<global::Customer.DAL.Models.PointReservation>, GenericRepository<global::Customer.DAL.Models.PointReservation, CustomerDbContext>>();

        return services;
    }
}
