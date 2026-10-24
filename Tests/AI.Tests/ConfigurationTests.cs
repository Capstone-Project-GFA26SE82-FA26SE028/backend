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
        Assert.Equal("qwen3:4b-instruct-2507-q4_K_M", provider.GetRequiredService<IOptions<OllamaSettings>>().Value.Model);
    }

    [Theory]
    [InlineData("Ollama:Model", "different-model", true)]
    [InlineData("Ollama:Endpoint", "file:///tmp/model", true)]
    [InlineData("AI:MaximumConcurrentInferences", "0", false)]
    [InlineData("AI:RequestTimeoutSeconds", "91", false)]
    [InlineData("AI:DuplicatePolicy", "ItemAndOptions", false)]
    public void InvalidConfigurationFailsClearly(string key, string value, bool ollama)
    {
        var services = new ServiceCollection().AddLogging();
        services.AddAssistant(Configuration((key, value)));
        using var provider = services.BuildServiceProvider();
        Assert.Throws<OptionsValidationException>(() =>
        {
            if (ollama)
            {
                _ = provider.GetRequiredService<IOptions<OllamaSettings>>().Value;
            }
            else
            {
                _ = provider.GetRequiredService<IOptions<AISettings>>().Value;
            }
        });
    }
}
