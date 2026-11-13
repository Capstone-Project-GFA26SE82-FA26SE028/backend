using System.Net;
using System.Text;
using System.Text.Json;
using AI.API.Clients;
using AI.API.Extensions;
using AI.BLL.Interfaces;
using AI.Common.DTOs;
using AI.Common.Exceptions;
using AI.Common.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AI.Tests;

public sealed class GeminiReadinessTests
{
    private const string TestKey = "test-gemini-key";

    [Fact]
    public async Task RecommendationReasonsUseControlledTokensWithoutFreeFormClaims()
    {
        var data = new AssistantTestContext();
        using var capacity = new AssistantCapacity(Options.Create(data.Settings));
        using var http = Http(async (request, ct) =>
        {
            using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            var allowed = json.RootElement.GetProperty("generationConfig").GetProperty("responseJsonSchema")
                .GetProperty("properties").GetProperty("suggestions").GetProperty("items")
                .GetProperty("properties").GetProperty("reason").GetProperty("enum").EnumerateArray().Select(value => value.GetString()!).ToArray();
            Assert.Equal(RecommendationReasons.Allowed, allowed);
            return Response(JsonSerializer.Serialize(new
            {
                suggestions = new[]
                {
                    new
                    {
                        key = "m0",
                        reason = RecommendationReasons.Allowed[0]
                    }
                }
            }));
        });

        var result = await Client(http, capacity, data).RecommendAsync(new RecommendationModelRequestDTO(
            [new ModelCartLineDTO("Rice", "Food", 1)], [new ModelItemDTO("m0", "Tea", "Drinks", null, 15000, true)], 1), default);

        Assert.Equal("m0", Assert.Single(result).Key);
    }

    [Theory]
    [InlineData("not-json")]
    [InlineData("{\"candidates\":[]}")]
    [InlineData("{\"candidates\":[{\"finishReason\":\"MAX_TOKENS\"}]}")]
    [InlineData("{\"candidates\":[{\"finishReason\":\"SAFETY\"}]}")]
    [InlineData("{\"candidates\":[{\"finishReason\":\"STOP\",\"content\":{\"parts\":[{\"functionCall\":{\"name\":\"createOrder\"}}]}}]}")]
    public async Task InvalidOrToolResponsesAreRejectedAndReleaseCapacity(string body)
    {
        var data = new AssistantTestContext();
        using var capacity = new AssistantCapacity(Options.Create(data.Settings));
        using var http = Http((_, _) => Task.FromResult(Raw(body)));

        var error = await Assert.ThrowsAsync<AIException>(() => Client(http, capacity, data).ChatAsync(new ChatModelRequestDTO("Hello", [], []), default));

        Assert.Equal(502, error.StatusCode);
        Assert.True(capacity.TryEnter(default));
        capacity.Exit();
    }

    [Theory]
    [InlineData(200, true, "Ready")]
    [InlineData(403, false, "CredentialsRejected")]
    [InlineData(429, false, "RateLimited")]
    [InlineData(503, false, "ProviderUnavailable")]
    public async Task ReadinessChecksConfiguredModelMetadataWithoutGeneratingTokens(int code, bool ready, string expected)
    {
        var data = new AssistantTestContext();
        using var capacity = new AssistantCapacity(Options.Create(data.Settings));
        using var http = Http((request, _) =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("/v1beta/models/gemini-3.1-flash-lite", request.RequestUri!.AbsolutePath);
            return Task.FromResult(Raw("{\"name\":\"models/gemini-3.1-flash-lite\",\"supportedGenerationMethods\":[\"generateContent\"]}", (HttpStatusCode)code));
        });

        var status = await Client(http, capacity, data).GetStatusAsync(default);

        Assert.Equal("Gemini", status.Provider);
        Assert.Equal(ready, status.Ready);
        Assert.Equal(expected, status.Status);
    }

    [Fact]
    public void DependencyInjectionHasExactlyTheGeminiProvider()
    {
        var services = new ServiceCollection().AddLogging();
        services.AddAssistant(ConfigurationTests.Configuration(("GEMINI_API_KEY", TestKey)));
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        Assert.IsType<GeminiModelClient>(Assert.Single(scope.ServiceProvider.GetServices<IAssistantModelClient>()));
        Assert.Equal(TestKey, scope.ServiceProvider.GetRequiredService<IOptions<GeminiSettings>>().Value.ApiKey);
    }

    [Fact]
    public async Task UnknownSuggestionFieldsAreRejectedBeforeReturningModelContent()
    {
        var data = new AssistantTestContext();
        using var capacity = new AssistantCapacity(Options.Create(data.Settings));
        using var http = Http((_, _) => Task.FromResult(Response(JsonSerializer.Serialize(new
        {
            suggestions = new[]
            {
                new
                {
                    key = "m0",
                    reason = RecommendationReasons.Allowed[0],
                    price = 1
                }
            }
        }))));

        var error = await Assert.ThrowsAsync<AIException>(() => Client(http, capacity, data).RecommendAsync(
            new RecommendationModelRequestDTO([], [new ModelItemDTO("m0", "Tea", "Drinks", null, 45000, true)], 1), default));

        Assert.Equal(502, error.StatusCode);
        Assert.True(capacity.TryEnter(default));
        capacity.Exit();
    }

    private static GeminiSettings Settings()
    {
        var settings = ClientContractTests.ModelSettings();
        settings.ApiKey = TestKey;
        return settings;
    }

    private static GeminiModelClient Client(HttpClient http, AssistantCapacity capacity, AssistantTestContext data, GeminiSettings? settings = null)
    {
        return new GeminiModelClient(http, capacity, Options.Create(settings ?? Settings()), Options.Create(data.Settings),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<GeminiModelClient>.Instance);
    }

    private static HttpClient Http(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send)
    {
        return new HttpClient(new Handler(send))
        {
            BaseAddress = new Uri("https://generativelanguage.googleapis.com/v1beta/"),
            Timeout = Timeout.InfiniteTimeSpan,
            MaxResponseContentBufferSize = 1024 * 1024
        };
    }

    private static HttpResponseMessage Response(string json)
    {
        return Raw(JsonSerializer.Serialize(new
        {
            candidates = new[]
            {
                new
                {
                    finishReason = "STOP",
                    content = new
                    {
                        parts = new[]
                        {
                            new
                            {
                                text = json
                            }
                        }
                    }
                }
            }
        }));
    }

    private static HttpResponseMessage Raw(string body, HttpStatusCode status = HttpStatusCode.OK)
    {
        return new HttpResponseMessage(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
    }

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            return send(request, ct);
        }
    }
}
