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
    public static IServiceCollection AddBillingServices(this IServiceCollection services, IConfiguration configuration)
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
        services.AddGrpcClient<Backend.Contracts.Ordering.BillingOrderGrpc.BillingOrderGrpcClient>(options =>
        {
            var orderingAddress = configuration["GrpcServices:Ordering"];
            if (!Uri.TryCreate(orderingAddress, UriKind.Absolute, out var orderingUri)
                || (orderingUri.Scheme != Uri.UriSchemeHttp && orderingUri.Scheme != Uri.UriSchemeHttps))
            {
                throw new InvalidOperationException("Configure GrpcServices:Ordering.");
            }

            options.Address = orderingUri;
        });
        services.AddScoped<IBillSourceClient, BillSourceClient>();
        services.Configure<BillSettings>(configuration.GetSection("Billing"));
        services.AddScoped<IBillingAccess, RestaurantBillingAccess>();
        services.AddScoped<ICashRegisterService, CashRegisterService>();
        services.AddScoped<ICashShiftService, CashShiftService>();
        services.AddScoped<IBillService, BillService>();
        services.AddSingleton<TimeProvider>(TimeProvider.System);
        return services;
    }
}
