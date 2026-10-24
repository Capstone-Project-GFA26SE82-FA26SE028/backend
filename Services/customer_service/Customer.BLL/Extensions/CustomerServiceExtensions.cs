using Customer.BLL.Implements;
using Customer.BLL.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace Customer.BLL.Extensions;

public static class CustomerServiceExtensions
{
    public static IServiceCollection AddCustomerBusinessServices(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<ICustomerLoyaltyService, CustomerLoyaltyService>();
        services.AddScoped<IFeedbackService, FeedbackService>();
        return services;
    }
}

