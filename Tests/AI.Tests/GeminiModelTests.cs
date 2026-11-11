using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AI.API.Clients;
using AI.BLL.Interfaces;
using AI.Common.DTOs;
using AI.Common.Exceptions;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AI.Tests;

public sealed class GeminiModelTests
{
    [Theory]
    [InlineData(400, 503)]
    [InlineData(401, 503)]
    [InlineData(403, 503)]
    [InlineData(404, 503)]
    [InlineData(429, 429)]
    [InlineData(500, 503)]
    public async Task ProviderErrorsAreControlledAndDoNotExposeProviderBody(int status, int expected)
    {
        using var http = Http(new Handler((_, _) => Task.FromResult(new HttpResponseMessage((HttpStatusCode)status)
        {
            Content = new StringContent("private provider details")
        })));
        var data = new AssistantTestContext();
        using var capacity = new AssistantCapacity(Options.Create(data.Settings));
        var error = await Assert.ThrowsAsync<AIException>(() => Model(http, capacity, data)
            .ChatAsync(new ChatModelRequestDTO("Hello", [], []), default));
        Assert.Equal(expected, error.StatusCode);
        Assert.DoesNotContain("private", error.Message);
    }

    [Theory]
    [InlineData("MAX_TOKENS")]
    [InlineData("SAFETY")]
    [InlineData("RECITATION")]
    public async Task IncompleteOrBlockedCandidateDoesNotReturnPartialAnswer(string finishReason)
    {
        using var http = Http(new Handler((_, _) => Task.FromResult(Response(new { intent = "menu" }, finishReason))));
        var data = new AssistantTestContext();
        using var capacity = new AssistantCapacity(Options.Create(data.Settings));
        var error = await Assert.ThrowsAsync<AIException>(() => Model(http, capacity, data)
            .ChatAsync(new ChatModelRequestDTO("Hello", [], []), default));
        Assert.Equal(502, error.StatusCode);
    }

    [Fact]
    public async Task PromptSafetyBlockIsNotExposedAsAnAnswer()
    {
        using var http = Http(new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new
            {
                promptFeedback = new
                {
                    blockReason = "SAFETY"
                }
            })
        })));
        var data = new AssistantTestContext();
        using var capacity = new AssistantCapacity(Options.Create(data.Settings));
        var error = await Assert.ThrowsAsync<AIException>(() => Model(http, capacity, data)
            .ChatAsync(new ChatModelRequestDTO("Hello", [], []), default));
        Assert.Equal(502, error.StatusCode);
    }

    [Fact]
    public async Task TimeoutCancelsHttpAndReleasesCapacityWithoutRetry()
    {
        var calls = 0;
        using var http = Http(new Handler(async (_, ct) =>
        {
            calls++;
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return Response(new { intent = "menu" });
        }));
        var data = new AssistantTestContext();
        data.Settings.RequestTimeoutSeconds = 1;
        using var capacity = new AssistantCapacity(Options.Create(data.Settings));
        var error = await Assert.ThrowsAsync<AIException>(() => Model(http, capacity, data)
            .ChatAsync(new ChatModelRequestDTO("Hello", [], []), default));
        Assert.Equal(504, error.StatusCode);
        Assert.Equal(1, calls);
        Assert.True(capacity.TryEnter(default));
        capacity.Exit();
    }

    [Fact]
    public async Task MissingKeyDoesNotContactProvider()
    {
        using var http = Http(new Handler((_, _) => throw new InvalidOperationException("Must not call provider")));
        var data = new AssistantTestContext();
        using var capacity = new AssistantCapacity(Options.Create(data.Settings));
        var settings = ClientContractTests.ModelSettings();
        settings.ApiKey = "";
        var model = new GeminiModelClient(http, capacity, Options.Create(settings), Options.Create(data.Settings));
        var error = await Assert.ThrowsAsync<AIException>(() => model.ChatAsync(new ChatModelRequestDTO("Hello", [], []), default));
        Assert.Equal(503, error.StatusCode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GatewayRunsBothUseCasesWithGeminiAndRejectsInventedKeys(bool invalidKey)
    {
        var data = new AssistantTestContext();
        var calls = 0;
        using var http = Http(new Handler(async (request, ct) =>
        {
            calls++;
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.EndsWith("/models/gemini-3.1-flash-lite:generateContent", request.RequestUri!.AbsolutePath);
            Assert.Equal("test-key", request.Headers.GetValues("x-goog-api-key").Single());
            var text = await request.Content!.ReadAsStringAsync(ct);
            Assert.DoesNotContain(data.Guest.Token, text);
            using var body = JsonDocument.Parse(text);
            Assert.False(body.RootElement.TryGetProperty("tools", out _));
            Assert.Equal("application/json", body.RootElement.GetProperty("generationConfig").GetProperty("responseMimeType").GetString());
            var properties = body.RootElement.GetProperty("generationConfig").GetProperty("responseJsonSchema").GetProperty("properties");
            using var inputBody = JsonDocument.Parse(body.RootElement.GetProperty("contents")[0].GetProperty("parts")[0].GetProperty("text").GetString()!);
            var input = inputBody.RootElement;
            if (input.TryGetProperty("verifiedMenu", out var verified))
            {
                return Response(new
                {
                    answer = "You can enjoy this dish with your meal.",
                    usedKeys = new[] { verified[0].GetProperty("key").GetString() }
                });
            }

            var recommendation = input.TryGetProperty("candidates", out var candidates);
            var key = invalidKey ? "invented-key" : recommendation
                ? candidates[0].GetProperty("key").GetString()!
                : input.GetProperty("menu")[0].GetProperty("key").GetString()!;
            if (recommendation)
            {
                return Response(new
                {
                    suggestions = new[] { new { key, reason = RecommendationReasons.Allowed[0] } }
                });
            }

            Assert.False(properties.TryGetProperty("answer", out _));
            Assert.False(properties.TryGetProperty("usedKeys", out _));
            Assert.True(properties.TryGetProperty("language", out _));
            Assert.True(properties.TryGetProperty("topic", out _));

            return Response(new
            {
                intent = "menu",
                language = "en",
                topic = "overview",
                answer = "This costs 1 VND and is safe for peanut allergies.",
                usedKeys = new[] { key },
                informationKeys = new[] { key },
                recommendationKeys = Array.Empty<string>(),
                maximumPrice = (decimal?)null
            });
        }));
        using var capacity = new AssistantCapacity(Options.Create(data.Settings));
        await using var ai = await GrpcFlowTests.Start(data, services => services.AddSingleton<IAssistantModelClient>(Model(http, capacity, data)));
        await using var gateway = await GatewayFlowTests.Start(ai);
        using var client = gateway.GetTestClient();
        client.DefaultRequestHeaders.Add("Cookie", $"guest_access_{data.Guest.TableSessionId:N}={data.Guest.Token}");
        var root = $"/api/ai/table-sessions/{data.Guest.TableSessionId}";
        using var chat = await client.PostAsJsonAsync(root + "/chat", new
        {
            message = "Is beef rice available?",
            history = Array.Empty<object>()
        });
        using var suggestions = await client.PostAsJsonAsync(root + $"/carts/{data.Cart.CartId}/recommendations", new
        {
            limit = 3
        });
        var expected = invalidKey ? HttpStatusCode.BadGateway : HttpStatusCode.OK;
        Assert.Equal(expected, chat.StatusCode);
        Assert.Equal(expected, suggestions.StatusCode);
        Assert.Equal(2, calls);
        if (!invalidKey)
        {
            using var result = JsonDocument.Parse(await chat.Content.ReadAsStringAsync());
            Assert.Equal("Beef rice has a base price of 45000 VND. Beef rice is currently orderable according to the menu.",
                result.RootElement.GetProperty("result").GetProperty("answer").GetString());
        }
    }

    private static GeminiModelClient Model(HttpClient http, AssistantCapacity capacity, AssistantTestContext data)
    {
        return new GeminiModelClient(http, capacity, Options.Create(ClientContractTests.ModelSettings()), Options.Create(data.Settings));
    }

    private static HttpClient Http(HttpMessageHandler handler)
    {
        return new HttpClient(handler)
        {
            BaseAddress = new Uri("https://generativelanguage.googleapis.com/v1beta/"),
            Timeout = Timeout.InfiniteTimeSpan
        };
    }

    private static HttpResponseMessage Response(object value, string finishReason = "STOP")
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new
            {
                candidates = new[]
                {
                    new
                    {
                        finishReason,
                        content = new
                        {
                            parts = new[]
                            {
                                new
                                {
                                    text = JsonSerializer.Serialize(value)
                                }
                            }
                        }
                    }
                }
            })
        };
    }

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return send(request, cancellationToken);
        }
    }
}
