using AI.API.Clients;
using AI.API.Extensions;
using AI.BLL.Interfaces;
using AI.Common.Settings;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AI.Tests;

public sealed class ConfigurationTests
{
    public static IConfiguration Configuration(params (string Key, string Value)[] changes)
    {
        return new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(AppContext.BaseDirectory, "ai-settings.json"))
            .AddInMemoryCollection(changes.Select(change => new KeyValuePair<string, string?>(change.Key, change.Value)))
            .Build();
    }

    [Fact]
    public void ApprovedLimitsAndModelAreConfigured()
    {
        var services = new ServiceCollection().AddLogging();
        services.AddAssistant(Configuration());
        using var provider = services.BuildServiceProvider();
        var limits = provider.GetRequiredService<IOptions<AISettings>>().Value;
        Assert.Equal(90, limits.RequestTimeoutSeconds);
        Assert.Equal(10, limits.DependencyTimeoutSeconds);
        Assert.Equal(1, limits.MaximumConcurrentInferences);
        Assert.Equal("ItemId", limits.DuplicatePolicy);
        var gemini = provider.GetRequiredService<IOptions<GeminiSettings>>().Value;
        Assert.Equal("gemini-3.1-flash-lite", gemini.Model);
        Assert.Equal(5, gemini.ReadinessTimeoutSeconds);
        using var scope = provider.CreateScope();
        Assert.IsType<GeminiModelClient>(scope.ServiceProvider.GetRequiredService<IAssistantModelClient>());
    }

    [Theory]
    [InlineData("Gemini:Model", "different-model", true)]
    [InlineData("Gemini:Endpoint", "file:///tmp/model", true)]
    [InlineData("Gemini:Endpoint", "https://other.example", true)]
    [InlineData("Gemini:Endpoint", "https://user@generativelanguage.googleapis.com", true)]
    [InlineData("Gemini:Endpoint", "https://generativelanguage.googleapis.com/?query=secret", true)]
    [InlineData("Gemini:ReadinessTimeoutSeconds", "6", true)]
    [InlineData("Gemini:Model", "gemini-../escape", true)]
    [InlineData("AI:MaximumConcurrentInferences", "0", false)]
    [InlineData("AI:RequestTimeoutSeconds", "91", false)]
    [InlineData("AI:DuplicatePolicy", "ItemAndOptions", false)]
    public void InvalidConfigurationFailsClearly(string key, string value, bool gemini)
    {
        var services = new ServiceCollection().AddLogging();
        services.AddAssistant(Configuration((key, value)));
        using var provider = services.BuildServiceProvider();
        Assert.Throws<OptionsValidationException>(() =>
        {
            if (gemini)
            {
                _ = provider.GetRequiredService<IOptions<GeminiSettings>>().Value;
            }
            else
            {
                _ = provider.GetRequiredService<IOptions<AISettings>>().Value;
            }
        });
    }
}
