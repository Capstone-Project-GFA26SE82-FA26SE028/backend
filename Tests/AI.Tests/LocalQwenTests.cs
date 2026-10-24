using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using AI.API.Clients;
using AI.BLL.Interfaces;
using Backend.Contracts.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit.Abstractions;

namespace AI.Tests;

public sealed class LocalQwenTests(ITestOutputHelper output)
{
    [LocalModelFact]
    public async Task ColdAndWarmQwenThroughAiGrpcWithPublicMenuFixtures()
    {
        var data = new AssistantTestContext();
        using var http = new HttpClient
        {
            BaseAddress = new Uri(ClientContractTests.ModelSettings().Endpoint + "/"),
            Timeout = Timeout.InfiniteTimeSpan,
            MaxResponseContentBufferSize = 1024 * 1024
        };
        using var capacity = new OllamaCapacity(Options.Create(data.Settings));
        var model = new OllamaModelClient(http, capacity, Options.Create(ClientContractTests.ModelSettings()), Options.Create(data.Settings));
        using var total = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        using var tags = await http.GetAsync("api/tags", total.Token);
        tags.EnsureSuccessStatusCode();
        using var unload = await http.PostAsJsonAsync("api/generate", new
        {
            model = ClientContractTests.ModelSettings().Model,
            keep_alive = 0
        }, total.Token);
        unload.EnsureSuccessStatusCode();
        await using var host = await GrpcFlowTests.Start(data, services => services.AddSingleton<IAssistantModelClient>(model));
        using var channel = GrpcFlowTests.Channel(host);
        var watch = Stopwatch.StartNew();
        var chat = await new ChatGrpc.ChatGrpcClient(channel).ChatAsync(GrpcFlowTests.Question(data), cancellationToken: total.Token);
        var cold = watch.Elapsed.TotalMilliseconds;
        Assert.Contains(chat.Items, item => item.ItemName == "Beef rice");
        watch.Restart();
        var warmChat = await new ChatGrpc.ChatGrpcClient(channel).ChatAsync(GrpcFlowTests.Question(data), cancellationToken: total.Token);
        var warm = watch.Elapsed.TotalMilliseconds;
        Assert.Contains(warmChat.Items, item => item.ItemName == "Beef rice");
        watch.Restart();
        var recommendations = await new RecommendationGrpc.RecommendationGrpcClient(channel).RecommendAsync(new RecommendationRequest
        {
            Access = GrpcFlowTests.Access(data),
            CartId = data.Cart.CartId.ToString(),
            Limit = 3
        }, cancellationToken: total.Token);
        var recommendationTime = watch.Elapsed.TotalMilliseconds;
        Assert.NotEmpty(recommendations.Items);
        Assert.DoesNotContain(recommendations.Items, item => item.Item.ItemId == data.Menu[0].ItemId.ToString());
        var allergy = GrpcFlowTests.Question(data);
        allergy.Message = "I have a peanut allergy. What is safe?";
        var fallback = await new ChatGrpc.ChatGrpcClient(channel).ChatAsync(allergy, cancellationToken: total.Token);
        Assert.Contains("staff", fallback.Answer);
        Assert.Empty(fallback.Items);
        var unrelated = GrpcFlowTests.Question(data);
        unrelated.Message = "Write a Python web scraper";
        var refused = await new ChatGrpc.ChatGrpcClient(channel).ChatAsync(unrelated, cancellationToken: total.Token);
        Assert.Empty(refused.Items);
        Assert.Contains("public menu", refused.Answer);
        var report = new
        {
            model = ClientContractTests.ModelSettings().Model,
            measuredAt = DateTimeOffset.UtcNow,
            coldChatMilliseconds = cold,
            warmChatMilliseconds = warm,
            warmRecommendationsMilliseconds = recommendationTime,
            scope = "AI gRPC + BLL + real local Ollama; QR, Catalog and Cart use public-data test fixtures. Not Gateway or production dependencies.",
            coldChatAnswer = chat.Answer,
            warmChatAnswer = warmChat.Answer,
            recommendedNames = recommendations.Items.Select(item => item.Item.ItemName).ToArray(),
            tests = new[] { "UC2 menu", "UC1 no duplicate cart item", "allergy fallback", "out-of-scope refusal" }
        };
        var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
        output.WriteLine(json);
        var path = Environment.GetEnvironmentVariable("AI_BENCHMARK_OUTPUT");
        if (!string.IsNullOrWhiteSpace(path))
        {
            await File.WriteAllTextAsync(path, json);
        }
    }

    private sealed class LocalModelFactAttribute : FactAttribute
    {
        public LocalModelFactAttribute()
        {
            if (Environment.GetEnvironmentVariable("RUN_LOCAL_AI_TESTS") != "1")
            {
                Skip = "Set RUN_LOCAL_AI_TESTS=1 and start the approved local Ollama model to run this benchmark.";
            }
        }
    }
}
