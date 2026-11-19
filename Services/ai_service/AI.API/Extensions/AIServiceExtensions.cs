using AI.API.Clients;
using AI.API.GrpcServices;
using AI.BLL.Implements;
using AI.BLL.Interfaces;
using AI.Common.Settings;
using Backend.Contracts.Catalog;
using Backend.Contracts.Ordering;
using Backend.Contracts.Restaurant;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AI.API.Extensions;

public static class AIServiceExtensions
{
    public static IServiceCollection AddAssistant(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<AISettings>().Bind(configuration.GetSection("AI"))
            .Validate(value => value.Channel == "DineIn" && value.DuplicatePolicy == "ItemId", "Configure the approved channel and ItemId duplicate policy.")
            .Validate(value => value.RequestTimeoutSeconds is > 0 and <= 90 && value.DependencyTimeoutSeconds is > 0 and <= 10
                && value.MaximumConcurrentInferences == 1 && value.MaximumSuggestions is > 0 and <= 3
                && value.MaximumMenuItems is > 0 and <= 1000 && value.MenuPageSize is > 0 and <= 100
                && value.ModelCandidateCount is > 0 and <= 10 && value.ModelCandidateCount <= value.MaximumMenuItems
                && value.MaximumMessageLength is > 0 and <= 1000 && value.MaximumHistoryTurns is >= 0 and <= 6, "Invalid assistant limits.")
            .ValidateOnStart();
        services.AddOptions<GeminiSettings>().Bind(configuration.GetSection("Gemini"))
            .Validate(value => IsGeminiAddress(value.Endpoint) && IsGeminiModel(value.Model)
                && value.MaximumOutputTokens is >= 128 and <= 512
                && value.MaximumPromptCharacters is >= 1000 and <= 6000
                && value.ReadinessTimeoutSeconds is > 0 and <= 5
                && value.RequestTimeoutSeconds is > 0 and <= 20
                && value.RetryDelayMilliseconds is >= 100 and <= 1000, "Invalid Gemini configuration.")
            .ValidateOnStart();

        services.TryAddSingleton(TimeProvider.System);
        services.AddGrpc(options =>
        {
            options.Interceptors.Add<AIInterceptor>();
            options.MaxReceiveMessageSize = 32768;
        });
        var protection = services.AddDataProtection().SetApplicationName("RestaurantAssistant");
        var keyDirectory = configuration["AI:ChatKeyDirectory"];
        if (!string.IsNullOrWhiteSpace(keyDirectory))
        {
            protection.PersistKeysToFileSystem(new DirectoryInfo(keyDirectory));
        }

        services.AddSingleton<IChatContextToken, ChatContextToken>();
        services.AddScoped<IChatService, ChatService>();
        services.AddScoped<IRecommendationService, RecommendationService>();
        services.AddScoped<MenuContextReader>();
        services.AddScoped<IGuestAccessClient, GuestAccessClient>();
        services.AddScoped<ICatalogMenuClient, CatalogMenuClient>();
        services.AddScoped<ICartClient, OrderingCartClient>();
        services.AddSingleton<ModelCapacity>();
        services.AddHttpClient<GeminiModelClient>((provider, client) =>
        {
            var settings = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<GeminiSettings>>().Value;
            client.BaseAddress = new Uri(settings.Endpoint.TrimEnd('/') + "/");
            client.Timeout = Timeout.InfiniteTimeSpan;
            client.MaxResponseContentBufferSize = 1024 * 1024;
        }).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false })
            .RedactLoggedHeaders(["x-goog-api-key"]);
        services.AddScoped<IAssistantModelClient>(provider => provider.GetRequiredService<GeminiModelClient>());
        services.AddGrpcClient<GuestAccessGrpc.GuestAccessGrpcClient>(options =>
        {
            options.Address = Address(configuration, "Restaurant");
        });
        services.AddGrpcClient<RestaurantAccessGrpc.RestaurantAccessGrpcClient>(options =>
        {
            options.Address = Address(configuration, "Restaurant");
        });
        services.AddGrpcClient<MenuGrpc.MenuGrpcClient>(options =>
        {
            options.Address = Address(configuration, "Catalog");
        });
        services.AddGrpcClient<CartGrpc.CartGrpcClient>(options =>
        {
            options.Address = Address(configuration, "Ordering");
        });
        return services;
    }

    private static bool IsGeminiAddress(string? value)
    {
        return Uri.TryCreate(value, UriKind.Absolute, out var uri)
            && uri.Scheme == Uri.UriSchemeHttps && uri.Host == "generativelanguage.googleapis.com"
            && uri.IsDefaultPort && uri.AbsolutePath == "/"
            && string.IsNullOrEmpty(uri.UserInfo) && string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment);
    }

    private static bool IsGeminiModel(string? value)
    {
        return value is { Length: > 7 and <= 100 } && value.StartsWith("gemini-", StringComparison.Ordinal)
            && value.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '.');
    }

    private static Uri Address(IConfiguration configuration, string service)
    {
        var address = configuration[$"GrpcServices:{service}"];
        if (!IsHttpAddress(address))
        {
            throw new InvalidOperationException($"Configure GrpcServices:{service} with an HTTP(S) endpoint.");
        }

        return new Uri(address!);
    }

    private static bool IsHttpAddress(string? value)
    {
        return Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https"
            && string.IsNullOrEmpty(uri.UserInfo) && string.IsNullOrEmpty(uri.Query);
    }
}
