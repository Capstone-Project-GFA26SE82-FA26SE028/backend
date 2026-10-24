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
            .Validate(value => value.Channel == "DineIn" && value.DuplicatePolicy == "ItemId", "Configure the approved channel and ItemId duplicate policy.")
            .Validate(value => value.RequestTimeoutSeconds is > 0 and <= 90 && value.DependencyTimeoutSeconds is > 0 and <= 10
                && value.MaximumConcurrentInferences == 1 && value.MaximumSuggestions is > 0 and <= 3
                && value.MaximumMenuItems is > 0 and <= 1000 && value.MenuPageSize is > 0 and <= 100
                && value.ModelCandidateCount is > 0 and <= 10 && value.ModelCandidateCount <= value.MaximumMenuItems
                && value.MaximumMessageLength is > 0 and <= 1000 && value.MaximumHistoryTurns is >= 0 and <= 6, "Invalid assistant limits.")
            .ValidateOnStart();
        services.AddOptions<OllamaSettings>().Bind(configuration.GetSection("Ollama"))
            .Validate(value => value.Model == "qwen3:4b-instruct-2507-q4_K_M" && value.ContextLength == 2048, "Configure the approved Qwen model and context.")
            .Validate(value => IsHttpAddress(value.Endpoint) && value.MaximumOutputTokens is >= 128 and <= 512
                && value.MaximumPromptCharacters is >= 1000 and <= 6000 && value.KeepAliveMinutes is >= 0 and <= 60, "Invalid Ollama configuration.")
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
        services.AddSingleton<OllamaCapacity>();
        services.AddHttpClient<OllamaModelClient>((provider, client) =>
        {
            var settings = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<OllamaSettings>>().Value;
            client.BaseAddress = new Uri(settings.Endpoint.TrimEnd('/') + "/");
            client.Timeout = Timeout.InfiniteTimeSpan;
            client.MaxResponseContentBufferSize = 1024 * 1024;
        });
        services.AddScoped<IAssistantModelClient>(provider => provider.GetRequiredService<OllamaModelClient>());
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
