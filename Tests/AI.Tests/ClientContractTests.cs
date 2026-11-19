using System.Net;
using System.Text;
using System.Text.Json;
using AI.API.Clients;
using AI.Common.DTOs;
using AI.Common.Exceptions;
using AI.Common.Settings;
using Backend.Contracts.Catalog;
using Backend.Contracts.Ordering;
using Backend.Contracts.Restaurant;
using Grpc.Core;
using Microsoft.Extensions.Options;

namespace AI.Tests;

public sealed class ClientContractTests
{
    [Fact]
    public async Task ExistingGuestRpcsResolveRestaurantFromVerifiedQrAndShareDeadline()
    {
        var context = new AssistantTestContext();
        DateTime? validationDeadline = null;
        var invoker = new UnaryInvoker((method, request, options) =>
        {
            if (method == "/restaurant.RestaurantAccessGrpc/ResolveDataScope")
            {
                var scope = Assert.IsType<RestaurantDataScopeRequest>(request);
                Assert.Equal(context.Guest.TableSessionId.ToString(), scope.TableSessionId);
                Assert.Equal(context.Guest.Token, scope.GuestToken);
                Assert.Empty(scope.RestaurantId);
                Assert.True(scope.AllowCustomer);
                Assert.Equal(validationDeadline, options.Deadline);
                return new RestaurantDataScopeReply { RestaurantId = context.RestaurantId.ToString() };
            }

            Assert.Equal("/restaurant.GuestAccessGrpc/GetMe", method);
            validationDeadline = options.Deadline;
            var access = Assert.IsType<GuestAccessRequest>(request);
            Assert.Equal(context.Guest.Token, access.Token);
            Assert.Equal(context.Guest.TableSessionId.ToString(), access.TableSessionId);
            Assert.InRange(options.Deadline!.Value - DateTime.UtcNow, TimeSpan.FromSeconds(8), TimeSpan.FromSeconds(11));
            return new GuestAccessReply
            {
                TableSessionId = access.TableSessionId,
                GuestAccessId = Guid.NewGuid().ToString(),
                ExpiresAt = DateTimeOffset.UtcNow.AddHours(1).ToString("O")
            };
        });
        var client = new GuestAccessClient(new GuestAccessGrpc.GuestAccessGrpcClient(invoker),
            new RestaurantAccessGrpc.RestaurantAccessGrpcClient(invoker), Options.Create(context.Settings), TimeProvider.System);
        var guest = await client.VerifyAsync(context.Guest, default);
        Assert.Equal(context.RestaurantId, guest.RestaurantId);
    }

    [Fact]
    public async Task CartClientOnlyReadsExistingCartAndStripsPrivateQuote()
    {
        var context = new AssistantTestContext();
        var invoker = new UnaryInvoker((method, request, options) =>
        {
            Assert.Equal("/ordering.CartGrpc/GetById", method);
            Assert.Equal(context.RestaurantId.ToString(), options.Headers!.GetValue("x-restaurant-id"));
            var access = Assert.IsType<CartIdRequest>(request);
            Assert.Equal(context.Guest.Token, access.Access.GuestToken);
            return new CartInfo
            {
                CartId = access.CartId,
                RestaurantId = context.RestaurantId.ToString(),
                TableSessionId = access.Access.TableSessionId,
                Version = "actual-version",
                ExpiresAt = DateTimeOffset.UtcNow.AddHours(1).ToString("O")
            };
        });
        var client = new OrderingCartClient(new CartGrpc.CartGrpcClient(invoker), Options.Create(context.Settings));
        var result = await client.GetByIdAsync(context.Guest, context.RestaurantId, context.Cart.CartId, default);
        Assert.Equal("actual-version", result.Version);
        Assert.DoesNotContain("quote", JsonSerializer.Serialize(result), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CatalogRejectsWrongReturnedIdOrOrderableItemWithoutPrice(bool missingPrice)
    {
        var context = new AssistantTestContext();
        var invoker = new UnaryInvoker((method, request, options) =>
        {
            Assert.Equal("/catalog.MenuGrpc/GetById", method);
            Assert.Equal(context.RestaurantId.ToString(), options.Headers!.GetValue("x-restaurant-id"));
            var access = Assert.IsType<MenuItemRequest>(request);
            Assert.Equal(context.Guest.Token, access.Access.GuestToken);
            return new MenuItemReply
            {
                IsSuccess = true,
                StatusCode = 200,
                Result = new MenuItemInfo
                {
                    ItemId = missingPrice ? context.Menu[0].ItemId.ToString() : Guid.NewGuid().ToString(),
                    CategoryId = Guid.NewGuid().ToString(),
                    ItemName = "Tea",
                    Channel = "DineIn",
                    CanOrder = missingPrice,
                    EvaluatedAt = DateTimeOffset.UtcNow.ToString("O")
                }
            };
        });
        var client = new CatalogMenuClient(new MenuGrpc.MenuGrpcClient(invoker), Options.Create(context.Settings));
        var error = await Assert.ThrowsAsync<AIException>(() => client.GetByIdAsync(context.Guest, context.RestaurantId, "DineIn", context.Menu[0].ItemId, default));
        Assert.Equal(503, error.StatusCode);
    }

    [Fact]
    public async Task CatalogListsOnlyUnderTheServerVerifiedRestaurantScope()
    {
        var context = new AssistantTestContext();
        var invoker = new UnaryInvoker((method, request, options) =>
        {
            Assert.Equal("/catalog.MenuGrpc/GetAll", method);
            var menu = Assert.IsType<MenuListRequest>(request);
            Assert.Equal(context.Guest.TableSessionId.ToString(), menu.Access.TableSessionId);
            Assert.Equal(context.Guest.Token, menu.Access.GuestToken);
            Assert.Equal(context.RestaurantId.ToString(), options.Headers!.GetValue("x-restaurant-id"));
            Assert.False(menu.HasRestaurantId);
            return new MenuListReply { IsSuccess = true, StatusCode = 200 };
        });

        var client = new CatalogMenuClient(new MenuGrpc.MenuGrpcClient(invoker), Options.Create(context.Settings));
        var page = await client.GetAllAsync(context.Guest, context.RestaurantId, "DineIn", 1, 50, default);
        Assert.Empty(page.Items);
    }

    [Theory]
    [InlineData("not-json")]
    [InlineData("{\"candidates\":[]}")]
    [InlineData("{\"candidates\":[{\"finishReason\":\"MAX_TOKENS\"}]}")]
    [InlineData("{\"candidates\":[{\"finishReason\":\"STOP\",\"content\":{\"parts\":[{\"text\":\"private invalid output\"}]}}]}")]
    [InlineData("{\"candidates\":[{\"finishReason\":\"STOP\",\"content\":{\"parts\":[{\"text\":\"{\\\"intent\\\":\\\"menu\\\"}\"}]}}]}")]
    public async Task InvalidGeminiResponsesFailWithoutReturningModelText(string body)
    {
        var context = new AssistantTestContext();
        using var capacity = new ModelCapacity(Options.Create(context.Settings));
        using var http = Http(new Handler((_, _) => Task.FromResult(Response(body))));
        var client = Model(http, capacity, context);
        var error = await Assert.ThrowsAsync<AIException>(() => client.ChatAsync(new ChatModelRequestDTO("Hello", [], []), default));
        Assert.Equal(502, error.StatusCode);
        Assert.DoesNotContain("private", error.Message);
        Assert.True(capacity.TryEnter(default));
        capacity.Exit();
    }

    [Fact]
    public async Task ChatRequestUsesGeminiSchemaAndDoesNotTransmitGuestCredentials()
    {
        var context = new AssistantTestContext();
        using var capacity = new ModelCapacity(Options.Create(context.Settings));
        using var http = Http(new Handler(async (request, ct) =>
        {
            AssertGenerationRequest(request);
            var body = await request.Content!.ReadAsStringAsync(ct);
            Assert.DoesNotContain(context.Guest.Token, body);
            Assert.DoesNotContain(context.Guest.TableSessionId.ToString(), body);
            Assert.DoesNotContain(ModelSettings().ApiKey, body);
            using var json = JsonDocument.Parse(body);
            var config = json.RootElement.GetProperty("generationConfig");
            Assert.Equal(512, config.GetProperty("maxOutputTokens").GetInt32());
            Assert.Equal("application/json", config.GetProperty("responseMimeType").GetString());
            Assert.True(config.GetProperty("responseJsonSchema").GetProperty("properties").TryGetProperty("intent", out _));
            Assert.False(json.RootElement.TryGetProperty("tools", out _));
            return Completed("{\"intent\":\"out_of_scope\",\"language\":\"en\",\"preference\":\"none\",\"needsFoodVerification\":false,\"rememberFoodRestriction\":false,\"informationKeys\":[],\"recommendationKeys\":[],\"maximumPrice\":null}");
        }));
        var plan = await Model(http, capacity, context).ChatAsync(new ChatModelRequestDTO("Hello", [], []), default);
        Assert.Equal("out_of_scope", plan.Intent);
    }

    [Fact]
    public async Task RecommendationRequestUsesCartAndOnlySuppliedCandidateKeys()
    {
        var context = new AssistantTestContext();
        var data = new RecommendationModelRequestDTO(
            [new ModelCartLineDTO("Rice", "Main", 1)],
            [new ModelItemDTO("m0", "Tea", "Drinks", null, 10000, true)],
            1);
        using var capacity = new ModelCapacity(Options.Create(context.Settings));
        using var http = Http(new Handler(async (request, ct) =>
        {
            AssertGenerationRequest(request);
            using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            var root = json.RootElement;
            var input = root.GetProperty("contents")[0].GetProperty("parts")[0].GetProperty("text");
            using var payload = JsonDocument.Parse(input.GetString()!);
            Assert.Equal("Rice", payload.RootElement.GetProperty("cart")[0].GetProperty("name").GetString());
            var schema = root.GetProperty("generationConfig").GetProperty("responseJsonSchema")
                .GetProperty("properties").GetProperty("suggestions");
            Assert.Equal(1, schema.GetProperty("maxItems").GetInt32());
            var allowed = schema.GetProperty("items").GetProperty("properties").GetProperty("key").GetProperty("enum");
            Assert.Equal("m0", Assert.Single(allowed.EnumerateArray()).GetString());
            return Completed(JsonSerializer.Serialize(new
            {
                suggestions = new[] { new { key = "m0", reason = RecommendationReasons.Allowed[0] } }
            }));
        }));
        var choice = Assert.Single(await Model(http, capacity, context).RecommendAsync(data, default));
        Assert.Equal("m0", choice.Key);
    }

    [Fact]
    public async Task MissingApiKeyReturns503WithoutSendingProviderRequest()
    {
        var context = new AssistantTestContext();
        using var capacity = new ModelCapacity(Options.Create(context.Settings));
        using var http = Http(new Handler((_, _) => throw new InvalidOperationException("HTTP must not be called")));
        var settings = ModelSettings();
        settings.ApiKey = string.Empty;
        var client = new GeminiModelClient(http, capacity, Options.Create(settings), Options.Create(context.Settings));

        Assert.Equal(new ModelStatusDTO(false, "ApiKeyMissing"), await client.GetStatusAsync(default));
        var error = await Assert.ThrowsAsync<AIException>(() =>
            client.ChatAsync(new ChatModelRequestDTO("Hello", [], []), default));
        Assert.Equal(503, error.StatusCode);
    }

    [Theory]
    [InlineData(401, 503)]
    [InlineData(429, 429)]
    [InlineData(503, 503)]
    public async Task GeminiHttpErrorIsControlledAndDoesNotRetry(int providerStatus, int expectedStatus)
    {
        var context = new AssistantTestContext();
        var calls = 0;
        using var capacity = new ModelCapacity(Options.Create(context.Settings));
        using var http = Http(new Handler((_, _) =>
        {
            calls++;
            return Task.FromResult(Response("private provider content", (HttpStatusCode)providerStatus));
        }));
        var client = Model(http, capacity, context);
        var error = await Assert.ThrowsAsync<AIException>(() =>
            client.ChatAsync(new ChatModelRequestDTO("Hello", [], []), default));

        Assert.Equal(expectedStatus, error.StatusCode);
        Assert.Equal(1, calls);
        Assert.DoesNotContain("private", error.Message);
    }

    [Theory]
    [InlineData(200, "generateContent", true, "Ready")]
    [InlineData(200, "countTokens", false, "GenerationUnsupported")]
    [InlineData(403, "", false, "CredentialsRejected")]
    [InlineData(404, "", false, "ModelUnavailable")]
    [InlineData(429, "", false, "RateLimited")]
    public async Task ReadinessUsesGeminiMetadataAndDoesNotGenerate(
        int statusCode, string method, bool ready, string status)
    {
        var context = new AssistantTestContext();
        using var capacity = new ModelCapacity(Options.Create(context.Settings));
        using var http = Http(new Handler((request, _) =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("/v1beta/models/gemini-3.1-flash-lite", request.RequestUri!.PathAndQuery);
            Assert.Equal(ModelSettings().ApiKey, Assert.Single(request.Headers.GetValues("x-goog-api-key")));
            return Task.FromResult(Response(JsonSerializer.Serialize(new
            {
                supportedGenerationMethods = new[] { method }
            }), (HttpStatusCode)statusCode));
        }));
        Assert.Equal(new ModelStatusDTO(ready, status), await Model(http, capacity, context).GetStatusAsync(default));
    }

    [Fact]
    public async Task BusyModelReturnsImmediatelyWithoutQueueingAndCancellationReleasesSlot()
    {
        var context = new AssistantTestContext();
        using var capacity = new ModelCapacity(Options.Create(context.Settings));
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var http = Http(new Handler(async (_, ct) =>
        {
            entered.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return Response("{}");
        }));
        var client = Model(http, capacity, context);
        using var cancellation = new CancellationTokenSource();
        var first = client.ChatAsync(new ChatModelRequestDTO("Hello", [], []), cancellation.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var second = await Assert.ThrowsAsync<AIException>(() => client.ChatAsync(new ChatModelRequestDTO("Hello", [], []), default));
        Assert.Equal(429, second.StatusCode);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        Assert.True(capacity.TryEnter(default));
        capacity.Exit();
    }

    [Fact]
    public async Task GeminiNetworkFailureHasExplicitUnavailableError()
    {
        var context = new AssistantTestContext();
        using var capacity = new ModelCapacity(Options.Create(context.Settings));
        using var http = Http(new Handler((_, _) => throw new HttpRequestException("private details")));
        var error = await Assert.ThrowsAsync<AIException>(() => Model(http, capacity, context).ChatAsync(new ChatModelRequestDTO("Hello", [], []), default));
        Assert.Equal(503, error.StatusCode);
        Assert.DoesNotContain("private", error.Message);
    }

    [Fact]
    public async Task PromptLimitRejectsOversizedInputBeforeContactingModel()
    {
        var context = new AssistantTestContext();
        using var capacity = new ModelCapacity(Options.Create(context.Settings));
        using var http = Http(new Handler((_, _) => throw new InvalidOperationException("HTTP must not be called")));
        var error = await Assert.ThrowsAsync<AIException>(() => Model(http, capacity, context)
            .ChatAsync(new ChatModelRequestDTO(new string('x', 6000), [], []), default));
        Assert.Equal(413, error.StatusCode);
        Assert.True(capacity.TryEnter(default));
        capacity.Exit();
    }

    [Fact]
    public async Task ExpiredQrResponseCannotAuthenticateGuest()
    {
        var context = new AssistantTestContext();
        var invoker = new UnaryInvoker((_, _, _) => new GuestAccessReply
        {
            TableSessionId = context.Guest.TableSessionId.ToString(),
            GuestAccessId = Guid.NewGuid().ToString(),
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1).ToString("O")
        });
        var client = new GuestAccessClient(new GuestAccessGrpc.GuestAccessGrpcClient(invoker),
            new RestaurantAccessGrpc.RestaurantAccessGrpcClient(invoker), Options.Create(context.Settings), TimeProvider.System);
        var error = await Assert.ThrowsAsync<AIException>(() => client.VerifyAsync(context.Guest, default));
        Assert.Equal(401, error.StatusCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-guid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public async Task UnverifiedRestaurantFailsClosed(string restaurantId)
    {
        var context = new AssistantTestContext();
        var invoker = new UnaryInvoker((method, _, _) =>
        {
            if (method == "/restaurant.RestaurantAccessGrpc/ResolveDataScope")
            {
                return new RestaurantDataScopeReply { RestaurantId = restaurantId };
            }

            return new GuestAccessReply
            {
                TableSessionId = context.Guest.TableSessionId.ToString(),
                GuestAccessId = Guid.NewGuid().ToString(),
                ExpiresAt = DateTimeOffset.UtcNow.AddHours(1).ToString("O")
            };
        });
        var client = new GuestAccessClient(new GuestAccessGrpc.GuestAccessGrpcClient(invoker),
            new RestaurantAccessGrpc.RestaurantAccessGrpcClient(invoker), Options.Create(context.Settings), TimeProvider.System);
        var error = await Assert.ThrowsAsync<AIException>(() => client.VerifyAsync(context.Guest, default));
        Assert.Equal(503, error.StatusCode);
    }

    private static GeminiSettings ModelSettings()
    {
        return new GeminiSettings
        {
            Endpoint = "https://generativelanguage.googleapis.com",
            Model = "gemini-3.1-flash-lite",
            ApiKey = "fixture-key",
            MaximumOutputTokens = 512,
            MaximumPromptCharacters = 5000,
            ReadinessTimeoutSeconds = 5
        };
    }

    private static GeminiModelClient Model(HttpClient http, ModelCapacity capacity, AssistantTestContext context)
    {
        return new GeminiModelClient(http, capacity, Options.Create(ModelSettings()), Options.Create(context.Settings));
    }

    private static HttpClient Http(HttpMessageHandler handler)
    {
        return new HttpClient(handler)
        {
            BaseAddress = new Uri("https://generativelanguage.googleapis.com/"),
            Timeout = Timeout.InfiniteTimeSpan,
            MaxResponseContentBufferSize = 1024 * 1024
        };
    }

    private static void AssertGenerationRequest(HttpRequestMessage request)
    {
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("/v1beta/models/gemini-3.1-flash-lite:generateContent", request.RequestUri!.PathAndQuery);
        Assert.Equal(ModelSettings().ApiKey, Assert.Single(request.Headers.GetValues("x-goog-api-key")));
    }

    private static HttpResponseMessage Completed(string answer)
    {
        return Response(JsonSerializer.Serialize(new
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
                            new { text = "private reasoning", thought = true },
                            new { text = answer, thought = false }
                        }
                    }
                }
            }
        }));
    }

    private static HttpResponseMessage Response(string body, HttpStatusCode status = HttpStatusCode.OK)
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

    private sealed class UnaryInvoker(Func<string, object, CallOptions, object> reply) : CallInvoker
    {
        public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request)
        {
            var result = (TResponse)reply(method.FullName, request, options);
            return new AsyncUnaryCall<TResponse>(Task.FromResult(result), Task.FromResult(new Metadata()), () => Status.DefaultSuccess, () => new Metadata(), () => { });
        }

        public override TResponse BlockingUnaryCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request)
        {
            throw new NotSupportedException();
        }

        public override AsyncClientStreamingCall<TRequest, TResponse> AsyncClientStreamingCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options)
        {
            throw new NotSupportedException();
        }

        public override AsyncDuplexStreamingCall<TRequest, TResponse> AsyncDuplexStreamingCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options)
        {
            throw new NotSupportedException();
        }

        public override AsyncServerStreamingCall<TResponse> AsyncServerStreamingCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request)
        {
            throw new NotSupportedException();
        }
    }
}
