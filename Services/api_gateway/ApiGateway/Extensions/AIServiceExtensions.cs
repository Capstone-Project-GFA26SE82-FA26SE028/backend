using ApiGateway.Settings;
using Backend.Contracts.AI;

namespace ApiGateway.Extensions;

public static class AIServiceExtensions
{
    public static IServiceCollection AddAssistantClients(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<AIClientSettings>().Bind(configuration.GetSection("AIClient"))
            .Validate(value => value.TimeoutSeconds is > 0 and <= 90, "AIClient timeout must be within 1-90 seconds.")
            .ValidateOnStart();
        services.AddGrpcClient<ChatGrpc.ChatGrpcClient>(options =>
        {
            options.Address = Address(configuration);
        });
        services.AddGrpcClient<RecommendationGrpc.RecommendationGrpcClient>(options =>
        {
            options.Address = Address(configuration);
        });
        return services;
    }

    private static Uri Address(IConfiguration configuration)
    {
        if (!Uri.TryCreate(configuration["GrpcServices:AI"], UriKind.Absolute, out var address)
            || address.Scheme is not ("http" or "https"))
        {
            throw new InvalidOperationException("Configure GrpcServices:AI with an HTTP(S) address.");
        }

        return address;
    }
}
