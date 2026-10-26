using Backend.Contracts.Restaurant;
using Billing.API.Clients;
using Billing.API.GrpcServices;
using Billing.API.Settings;
using Billing.BLL.Interfaces;
using Billing.BLL.Implements;
using Billing.Common.Settings;

namespace Billing.API.Extensions;

public static class BillingServiceExtensions
{
    public static IServiceCollection AddCashRegisters(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddGrpc(options =>
        {
            options.Interceptors.Add<BillingInterceptor>();
        });
        services.AddHttpContextAccessor();
        services.AddOptions<CashRegisterAccessSettings>()
            .Bind(configuration.GetSection("CashRegisterAccess"))
            .Validate(value => value.ManagerRoleIds.Length > 0 && value.CashierRoleIds.Length > 0
                && value.ManagerRoleIds.All(role => !string.IsNullOrWhiteSpace(role))
                && value.CashierRoleIds.All(role => !string.IsNullOrWhiteSpace(role))
                && !value.ManagerRoleIds.Intersect(value.CashierRoleIds, StringComparer.Ordinal).Any(),
                "Configure separate manager and cashier role IDs for cash register access.")
            .ValidateOnStart();
        services.AddOptions<RestaurantAccessSettings>()
            .Bind(configuration.GetSection("RestaurantAccess"))
            .Validate(value => value.TimeoutSeconds is > 0 and <= 30, "Restaurant access timeout must be 1-30 seconds.")
            .ValidateOnStart();
        var address = configuration["GrpcServices:Restaurant"];
        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException("Configure GrpcServices:Restaurant.");
        }

        services.AddGrpcClient<RestaurantAccessGrpc.RestaurantAccessGrpcClient>(options =>
        {
            options.Address = uri;
        });
        services.AddScoped<IBillingAccess, RestaurantBillingAccess>();
        services.AddScoped<ICashRegisterService, CashRegisterService>();
        return services;
    }
}
