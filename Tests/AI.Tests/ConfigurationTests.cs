using AI.API.Extensions;
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
    public void ApprovedLimitsAndProviderAreConfigured()
    {
        var services = new ServiceCollection().AddLogging();
        services.AddAssistant(Configuration());
        using var provider = services.BuildServiceProvider();
        var limits = provider.GetRequiredService<IOptions<AISettings>>().Value;
        Assert.Equal(90, limits.RequestTimeoutSeconds);
        Assert.Equal(10, limits.DependencyTimeoutSeconds);
        Assert.Equal(1, limits.MaximumConcurrentInferences);
        Assert.Equal("ItemId", limits.DuplicatePolicy);
        Assert.Equal("https://generativelanguage.googleapis.com/v1beta", provider.GetRequiredService<IOptions<GeminiSettings>>().Value.Endpoint);
    }

    [Theory]
    [InlineData("Gemini:ThinkingLevel", "invalid", true)]
    [InlineData("Gemini:Endpoint", "file:///tmp/model", true)]
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
