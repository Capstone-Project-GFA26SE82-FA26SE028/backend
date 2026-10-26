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
            .ValidateOnStart();
        services.AddGrpcClient<TableSessionGrpc.TableSessionGrpcClient>(options =>
        {
            options.Address = Address(configuration, "Restaurant");
        });
        services.AddGrpcClient<OrderValidationGrpc.OrderValidationGrpcClient>(options =>
        {
            options.Address = Address(configuration, "Catalog");
        });
        services.AddGrpcClient<Backend.Contracts.Catalog.RecipeGrpc.RecipeGrpcClient>(options =>
        {
            options.Address = Address(configuration, "Catalog");
        });
        services.AddGrpcClient<Backend.Contracts.Inventory.StockIssueGrpc.StockIssueGrpcClient>(options =>
        {
            options.Address = Address(configuration, "Inventory");
        });
        services.AddScoped<ICookingInventoryClient, CookingInventoryClient>();
        services.AddScoped<IOrderingAccess, RestaurantOrderingAccess>();
        services.AddScoped<ICatalogOrderClient, CatalogOrderClient>();
        services.AddScoped<ICartService, CartService>();
        services.AddScoped<IOrderService, OrderService>();
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
