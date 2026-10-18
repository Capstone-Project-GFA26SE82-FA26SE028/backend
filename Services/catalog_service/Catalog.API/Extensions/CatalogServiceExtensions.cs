using Backend.Contracts.Identity;
using Catalog.API.Authorization;
using Catalog.BLL.Implements;
using Catalog.BLL.Interfaces;
using Catalog.Common.Settings;

namespace Catalog.API.Extensions;

public static class CatalogServiceExtensions
{
    public static IServiceCollection AddCatalogManagement(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddHttpContextAccessor();
        services.AddOptions<CatalogManagementSettings>()
            .Bind(configuration.GetSection("CatalogManagement"))
            .PostConfigure(settings =>
            {
                settings.MenuManagerRoleIds = RemoveEmptyValues(settings.MenuManagerRoleIds);
                settings.NonSellingItemAvailabilities = RemoveEmptyValues(
                    settings.NonSellingItemAvailabilities);
            })
            .Validate(settings => settings.MenuManagerRoleIds is not null
                && settings.MenuManagerRoleIds.All(IsValidValue),
                "MenuManagerRoleIds must contain actual, non-empty RoleIds.")
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
