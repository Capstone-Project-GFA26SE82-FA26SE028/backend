using Backend.Contracts.Identity;
using Catalog.API.Authorization;
using Catalog.BLL.Implements;
using Catalog.BLL.Interfaces;
using Catalog.Common.Settings;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Catalog.API.Extensions;

public static class CatalogServiceExtensions
{
    public static IServiceCollection AddCatalogManagement(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddHttpContextAccessor();
        services.AddSingleton(configuration.GetSection("Cloudinary")
            .Get<Shared.Common.Images.CloudinarySettings>() ?? new Shared.Common.Images.CloudinarySettings());
        services.AddHttpClient<Shared.Common.Images.IImageStorage, Shared.Common.Images.CloudinaryImageStorage>()
            .ConfigurePrimaryHttpMessageHandler(() =>
            {
                return new HttpClientHandler
                {
                    AllowAutoRedirect = false
                };
            });
        services.AddScoped<IItemImageService, ItemImageService>();
        services.AddGrpcClient<Backend.Contracts.Inventory.IngredientUnitGrpc.IngredientUnitGrpcClient>(options =>
        {
            options.Address = new Uri(configuration["GrpcServices:Inventory"]
                ?? throw new InvalidOperationException("Vui lòng cấu hình GrpcServices:Inventory."));
        });
        services.AddScoped<IInventoryRecipeLookup, InventoryRecipeLookup>();
        services.AddGrpcClient<Backend.Contracts.Ordering.KitchenStationGrpc.KitchenStationGrpcClient>(options =>
        {
            options.Address = new Uri(configuration["GrpcServices:Ordering"] ?? "https://localhost:5004");
        });
        services.AddScoped<IKitchenStationLookup, OrderingKitchenStationLookup>();
        services.TryAddSingleton<TimeProvider>(TimeProvider.System);
        services.Configure<PricingSettings>(configuration.GetSection("Pricing"));
        services.AddOptions<ItemManagementSettings>()
            .Bind(configuration.GetSection("ItemManagement"))
            .Validate(settings => settings.InitialAvailability is not null
                && settings.InitialAvailability.Length <= 50
                && settings.InitialAvailability == settings.InitialAvailability.Trim()
                && !settings.InitialAvailability.Any(char.IsControl),
                "ItemManagement:InitialAvailability phải có tối đa 50 ký tự, không có khoảng trắng ở đầu hoặc cuối và không chứa ký tự điều khiển.")
            .ValidateOnStart();
        services.AddOptions<CatalogManagementSettings>()
            .Bind(configuration.GetSection("CatalogManagement"))
            .PostConfigure(settings =>
            {
                settings.MenuManagerRoleIds = RemoveEmptyValues(settings.MenuManagerRoleIds);
                settings.MenuReaderRoleIds = RemoveEmptyValues(settings.MenuReaderRoleIds);
                settings.KitchenRoleIds = RemoveEmptyValues(settings.KitchenRoleIds);
                settings.NonSellingItemAvailabilities = RemoveEmptyValues(
                    settings.NonSellingItemAvailabilities);
            })
            .Validate(settings => settings.MenuManagerRoleIds is not null
                && settings.MenuManagerRoleIds.All(IsValidValue),
                "MenuManagerRoleIds phải chứa các RoleId có thực và khác rỗng.")
            .Validate(settings => settings.KitchenRoleIds.All(IsValidValue),
                "KitchenRoleIds phải chứa các RoleId có thực và khác rỗng.")
            .Validate(settings => settings.MenuReaderRoleIds.All(IsValidValue),
                "MenuReaderRoleIds phải chứa các RoleId có thực và khác rỗng.")
            .Validate(settings => settings.NonSellingItemAvailabilities is not null
                && settings.NonSellingItemAvailabilities.All(IsValidValue),
                "NonSellingItemAvailabilities phải chứa các giá trị trạng thái có thực.")
            .Validate(settings => settings.IdentityTimeoutSeconds > 0,
                "IdentityTimeoutSeconds phải lớn hơn 0.")
            .ValidateOnStart();

        var address = configuration["GrpcServices:Identity"];
        if (!Uri.TryCreate(address, UriKind.Absolute, out var identityUri)
            || (identityUri.Scheme != Uri.UriSchemeHttp
                && identityUri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException("Vui lòng cấu hình GrpcServices:Identity.");
        }

        services.AddGrpcClient<UserGrpc.UserGrpcClient>(options =>
        {
            options.Address = identityUri;
        });
        services.AddGrpcClient<Backend.Contracts.Restaurant.GuestAccessGrpc.GuestAccessGrpcClient>(options =>
        {
            var address = configuration["GrpcServices:Restaurant"];
            if (!Uri.TryCreate(address, UriKind.Absolute, out var restaurantUri)
                || (restaurantUri.Scheme != Uri.UriSchemeHttp && restaurantUri.Scheme != Uri.UriSchemeHttps))
            {
                throw new InvalidOperationException("Vui lòng cấu hình GrpcServices:Restaurant.");
            }

            options.Address = restaurantUri;
        });
        services.AddScoped<IMenuAccess, MenuAccess>();
        services.AddScoped<IMenuService, MenuService>();
        services.AddScoped<IOrderValidationService, OrderValidationService>();
        services.AddScoped<ICatalogAccess, IdentityCatalogAccess>();
        services.AddScoped<ICategoryService, CategoryService>();
        services.AddScoped<IItemService, ItemService>();
        services.AddScoped<IPriceService, PriceService>();
        services.AddScoped<OptionWriteExecutor>();
        services.AddScoped<IOptionGroupService, OptionGroupService>();
        services.AddScoped<IOptionService, OptionService>();
        services.AddScoped<IItemOptionGroupService, ItemOptionGroupService>();
        services.AddScoped<IRecipeService, RecipeService>();
        return services;
    }

    private static bool IsValidValue(string value)
    {
        return !string.IsNullOrWhiteSpace(value) && value == value.Trim();
    }

    private static string[] RemoveEmptyValues(string[]? values)
    {
        return values?.Where(value => !string.IsNullOrWhiteSpace(value)).ToArray() ?? [];
    }
}
