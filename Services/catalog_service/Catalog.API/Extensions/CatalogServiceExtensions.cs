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
        services.TryAddSingleton<TimeProvider>(TimeProvider.System);
        services.Configure<PricingSettings>(configuration.GetSection("Pricing"));
        services.AddOptions<ItemManagementSettings>()
            .Bind(configuration.GetSection("ItemManagement"))
            .Validate(settings => settings.InitialAvailability is not null
                && settings.InitialAvailability.Length <= 50
                && settings.InitialAvailability == settings.InitialAvailability.Trim()
                && !settings.InitialAvailability.Any(char.IsControl),
                "ItemManagement:InitialAvailability must be at most 50 characters without surrounding whitespace or control characters.")
            .ValidateOnStart();
        services.AddOptions<CatalogManagementSettings>()
            .Bind(configuration.GetSection("CatalogManagement"))
            .PostConfigure(settings =>
            {
                settings.MenuManagerRoleIds = RemoveEmptyValues(settings.MenuManagerRoleIds);
                settings.KitchenRoleIds = RemoveEmptyValues(settings.KitchenRoleIds);
                settings.NonSellingItemAvailabilities = RemoveEmptyValues(
                    settings.NonSellingItemAvailabilities);
            })
            .Validate(settings => settings.MenuManagerRoleIds is not null
                && settings.MenuManagerRoleIds.All(IsValidValue),
                "MenuManagerRoleIds must contain actual, non-empty RoleIds.")
            .Validate(settings => settings.KitchenRoleIds.All(IsValidValue),
                "KitchenRoleIds must contain actual, non-empty RoleIds.")
            .Validate(settings => settings.NonSellingItemAvailabilities is not null
                && settings.NonSellingItemAvailabilities.All(IsValidValue),
                "NonSellingItemAvailabilities must contain actual status values.")
            .Validate(settings => settings.IdentityTimeoutSeconds > 0,
                "IdentityTimeoutSeconds must be positive.")
            .ValidateOnStart();

        var address = configuration["GrpcServices:Identity"];
        if (!Uri.TryCreate(address, UriKind.Absolute, out var identityUri)
            || (identityUri.Scheme != Uri.UriSchemeHttp
                && identityUri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException("Configure GrpcServices:Identity.");
        }

        services.AddGrpcClient<UserGrpc.UserGrpcClient>(options =>
        {
            options.Address = identityUri;
        });
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
