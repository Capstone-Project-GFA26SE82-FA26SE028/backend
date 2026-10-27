using Backend.Contracts.Catalog;
using Backend.Contracts.Restaurant;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Ordering.API.Clients;
using Ordering.API.GrpcServices;
using Ordering.BLL.Implements;
using Ordering.BLL.Interfaces;
using Ordering.Common.Settings;

namespace Ordering.API.Extensions;

public static class OrderingServiceExtensions
{
    public static IServiceCollection AddOrderingServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddGrpc(options =>
        {
            options.Interceptors.Add<OrderingInterceptor>();
        });
        services.AddHttpContextAccessor();
        services.Configure<KitchenStationSettings>(configuration.GetSection("KitchenStationManagement"));
        services.AddGrpcClient<Backend.Contracts.Identity.UserGrpc.UserGrpcClient>(options =>
        {
            options.Address = Address(configuration, "Identity");
        });
        services.AddScoped<IKitchenStationAccess, IdentityKitchenStationAccess>();
        services.AddScoped<IKitchenStationService, KitchenStationService>();
        services.AddScoped<IKitchenTaskService, KitchenTaskService>();
        services.TryAddSingleton<TimeProvider>(TimeProvider.System);
        services.AddOptions<OrderingSettings>().Bind(configuration.GetSection("Ordering"))
            .Validate(settings => settings.DependencyTimeoutSeconds is > 0 and <= 60, "Dependency timeout must be 1-60 seconds.")
            .Validate(settings => settings.CheckoutCashierRoleIds.Length > 0
                && settings.CheckoutCashierRoleIds.All(role => !string.IsNullOrWhiteSpace(role)),
                "Configure Ordering:CheckoutCashierRoleIds.")
            .ValidateOnStart();
        services.AddGrpcClient<TableSessionGrpc.TableSessionGrpcClient>(options =>
        {
            options.Address = Address(configuration, "Restaurant");
        });
        services.AddGrpcClient<OrderValidationGrpc.OrderValidationGrpcClient>(options =>
        {
            options.Address = Address(configuration, "Catalog");
        });
        services.AddScoped<IOrderingAccess, RestaurantOrderingAccess>();
        services.AddScoped<ICatalogOrderClient, CatalogOrderClient>();
        services.AddScoped<ICartService, CartService>();
        services.AddScoped<IOrderService, OrderService>();
        services.AddGrpcClient<BillingSessionGrpc.BillingSessionGrpcClient>(options =>
        {
            options.Address = Address(configuration, "Restaurant");
        });
        services.AddScoped<IBillingSessionClient, BillingSessionClient>();
        services.AddScoped<IBillingOrderService, BillingOrderService>();
        services.AddGrpcClient<RestaurantAccessGrpc.RestaurantAccessGrpcClient>(options =>
        {
            options.Address = Address(configuration, "Restaurant");
        });
        services.AddGrpcClient<Backend.Contracts.Billing.BillCheckoutGrpc.BillCheckoutGrpcClient>(options =>
        {
            options.Address = Address(configuration, "Billing");
        });
        services.AddScoped<ICheckoutAccess, CheckoutAccess>();
        services.AddScoped<ICheckoutReleaseAuthorization, CheckoutReleaseAuthorization>();
        services.AddScoped<ICheckoutLockService, CheckoutLockService>();
        return services;
    }

    private static Uri Address(IConfiguration configuration, string service)
    {
        if (!Uri.TryCreate(configuration[$"GrpcServices:{service}"], UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException($"Configure GrpcServices:{service}.");
        }

        return uri;
    }
}
