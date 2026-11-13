using AI.API.Clients;
using AI.API.GrpcServices;
using AI.BLL.Implements;
using AI.BLL.Interfaces;
using AI.Common.Settings;
using Backend.Contracts.Catalog;
using Backend.Contracts.Ordering;
using Backend.Contracts.Restaurant;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AI.API.Extensions;

public static class AIServiceExtensions
{
    public static IServiceCollection AddAssistant(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<AISettings>().Bind(configuration.GetSection("AI"))
            .Validate(value => value.Channel == "DineIn" && value.Currency == "VND" && value.DuplicatePolicy == "ItemId", "Configure the approved channel, VND currency and ItemId duplicate policy.")
            .Validate(value => value.RequestTimeoutSeconds is > 0 and <= 90 && value.DependencyTimeoutSeconds is > 0 and <= 10
                && value.MaximumConcurrentInferences == 1 && value.MaximumSuggestions is > 0 and <= 3
                && value.MaximumMenuItems is > 0 and <= 1000 && value.MenuPageSize is > 0 and <= 100
                && value.ModelCandidateCount is > 0 and <= 10 && value.ModelCandidateCount <= value.MaximumMenuItems
                && value.MaximumMessageLength is > 0 and <= 1000 && value.MaximumHistoryTurns is >= 0 and <= 6, "Invalid assistant limits.")
            .ValidateOnStart();
        services.AddOptions<GeminiSettings>().Bind(configuration.GetSection("Gemini"))
            .PostConfigure(value =>
            {
                if (string.IsNullOrWhiteSpace(value.ApiKey))
                {
                    value.ApiKey = configuration["GEMINI_API_KEY"] ?? string.Empty;
                }
            })
            .Validate(value => Uri.TryCreate(value.Endpoint, UriKind.Absolute, out var address)
                && address.Scheme == "https" && string.IsNullOrEmpty(address.UserInfo)
                && address.Host == "generativelanguage.googleapis.com" && address.IsDefaultPort
                && address.AbsolutePath.TrimEnd('/') == "/v1beta"
                && string.IsNullOrEmpty(address.Query) && string.IsNullOrEmpty(address.Fragment),
                "Configure Gemini:Endpoint with the official HTTPS v1beta address.")
            .Validate(value => !string.IsNullOrWhiteSpace(value.Model)
                && value.Model.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.')
                && value.MaximumOutputTokens is >= 256 and <= 4096
                && double.IsFinite(value.Temperature) && value.Temperature is >= 0 and <= 2
                && value.ThinkingLevel == "minimal"
                && value.MaximumPromptCharacters is >= 1000 and <= 50000,
                "Invalid Gemini configuration.")
            .ValidateOnStart();
        services.TryAddSingleton(TimeProvider.System);
        services.AddGrpc(options =>
        {
            options.Interceptors.Add<AIInterceptor>();
            options.MaxReceiveMessageSize = 32768;
        });
        services.AddScoped<IChatService, ChatService>();
        services.AddScoped<IRecommendationService, RecommendationService>();
        services.AddScoped<MenuContextReader>();
        services.AddScoped<IGuestAccessClient, GuestAccessClient>();
        services.AddScoped<ICatalogMenuClient, CatalogMenuClient>();
        services.AddScoped<ICartClient, OrderingCartClient>();
        services.AddSingleton<AssistantCapacity>();
        services.AddHttpClient<GeminiModelClient>((provider, client) =>
        {
            var settings = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<GeminiSettings>>().Value;
            client.BaseAddress = new Uri(settings.Endpoint.TrimEnd('/') + "/");
            client.Timeout = Timeout.InfiniteTimeSpan;
            client.MaxResponseContentBufferSize = 1024 * 1024;
        })
        .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
        {
            AllowAutoRedirect = false
        })
        .RedactLoggedHeaders(["x-goog-api-key"]);
        services.AddScoped<IAssistantModelClient>(provider => provider.GetRequiredService<GeminiModelClient>());
        services.AddGrpcClient<GuestAccessGrpc.GuestAccessGrpcClient>(options =>
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
