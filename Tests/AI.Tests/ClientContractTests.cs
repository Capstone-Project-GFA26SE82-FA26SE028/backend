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
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

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
            new RestaurantAccessGrpc.RestaurantAccessGrpcClient(invoker), Options.Create(context.Settings), TimeProvider.System, NullLogger<GuestAccessClient>.Instance);
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
        var client = new OrderingCartClient(new CartGrpc.CartGrpcClient(invoker), Options.Create(context.Settings), NullLogger<OrderingCartClient>.Instance);
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
        var client = new CatalogMenuClient(new MenuGrpc.MenuGrpcClient(invoker), Options.Create(context.Settings), NullLogger<CatalogMenuClient>.Instance);
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

        var client = new CatalogMenuClient(new MenuGrpc.MenuGrpcClient(invoker), Options.Create(context.Settings), NullLogger<CatalogMenuClient>.Instance);
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
                suggestions = new[] { new { key = "m0", reason = "side" } }
            }));
        }));
        var choice = Assert.Single(await Model(http, capacity, context).RecommendAsync(data, default));
        Assert.Equal("m0", choice.Key);
        Assert.Equal(RecommendationReasons.Allowed[0], choice.Reason);
    }

    [Theory]
    [InlineData("side", 0)]
    [InlineData("extra", 1)]
    [InlineData("cart", 2)]
    public async Task CompactReasonCodesAreRenderedAsControlledVietnamese(string reason, int expected)
    {
        var context = new AssistantTestContext();
        using var capacity = new ModelCapacity(Options.Create(context.Settings));
        using var http = Http(new Handler((_, _) => Task.FromResult(Completed(JsonSerializer.Serialize(new
        {
            suggestions = new[] { new { key = "m0", reason } }
        })))));

        var result = await Model(http, capacity, context).RecommendAsync(
            new RecommendationModelRequestDTO([], [new ModelItemDTO("m0", "Tea", "Drinks", null, 10000, true)], 1), default);

        Assert.Equal(RecommendationReasons.Allowed[expected], Assert.Single(result).Reason);
    }

    [Theory]
    [InlineData("{\"suggestions\":[{\"key\":\"m0\",\"reason\":\"invented advice\"}]}")]
    [InlineData("{\"suggestions\":[null]}")]
    public async Task InvalidCompactRecommendationReasonFailsWithoutReturningModelContent(string json)
    {
        var context = new AssistantTestContext();
        using var capacity = new ModelCapacity(Options.Create(context.Settings));
        using var http = Http(new Handler((_, _) => Task.FromResult(Completed(json))));

        var error = await Assert.ThrowsAsync<AIException>(() => Model(http, capacity, context).RecommendAsync(
            new RecommendationModelRequestDTO([], [new ModelItemDTO("m0", "Tea", "Drinks", null, 10000, true)], 1), default));

        Assert.Equal(502, error.StatusCode);
        Assert.DoesNotContain("invented advice", error.Message);
    }

    [Fact]
    public async Task MissingApiKeyReturns503WithoutSendingProviderRequest()
    {
        var context = new AssistantTestContext();
        using var capacity = new ModelCapacity(Options.Create(context.Settings));
        using var http = Http(new Handler((_, _) => throw new InvalidOperationException("HTTP must not be called")));
        var settings = ModelSettings();
        settings.ApiKey = string.Empty;
        var client = new GeminiModelClient(http, capacity, Options.Create(settings), Options.Create(context.Settings), NullLogger<GeminiModelClient>.Instance);

        Assert.Equal(new ModelStatusDTO(false, "ApiKeyMissing"), await client.GetStatusAsync(default));
        var error = await Assert.ThrowsAsync<AIException>(() =>
            client.ChatAsync(new ChatModelRequestDTO("Hello", [], []), default));
        Assert.Equal(503, error.StatusCode);
    }

    [Theory]
    [InlineData(401, 503, 1)]
    [InlineData(429, 429, 1)]
    [InlineData(500, 503, 1)]
    [InlineData(503, 503, 2)]
    public async Task GeminiHttpErrorIsControlledAndOnly503RetriesOnce(int providerStatus, int expectedStatus, int expectedCalls)
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
        Assert.Equal(expectedCalls, calls);
        Assert.DoesNotContain("private", error.Message);
    }

    [Fact]
    public async Task Transient503RetriesOnceWithFreshRequestAndSamePayload()
    {
        var context = new AssistantTestContext();
        var requests = new List<HttpRequestMessage>();
        var payloads = new List<string>();
        using var capacity = new ModelCapacity(Options.Create(context.Settings));
        using var http = Http(new Handler(async (request, ct) =>
        {
            requests.Add(request);
            payloads.Add(await request.Content!.ReadAsStringAsync(ct));
            return requests.Count == 1 ? Response("private", HttpStatusCode.ServiceUnavailable) : Completed("{\"suggestions\":[]}");
        }));

        var result = await Model(http, capacity, context).RecommendAsync(new RecommendationModelRequestDTO([], [], 1), default);

        Assert.Empty(result);
        Assert.Equal(2, requests.Count);
        Assert.NotSame(requests[0], requests[1]);
        Assert.Equal(payloads[0], payloads[1]);
        Assert.True(capacity.TryEnter(default));
        capacity.Exit();
    }

    [Fact]
    public async Task RetryCanBeDisabledWithoutChanging503Response()
    {
        var context = new AssistantTestContext();
        var calls = 0;
        var settings = ModelSettings();
        settings.RetryServiceUnavailable = false;
        using var capacity = new ModelCapacity(Options.Create(context.Settings));
        using var http = Http(new Handler((_, _) =>
        {
            calls++;
            return Task.FromResult(Response("private", HttpStatusCode.ServiceUnavailable));
        }));
        var client = new GeminiModelClient(http, capacity, Options.Create(settings), Options.Create(context.Settings), NullLogger<GeminiModelClient>.Instance);

        var error = await Assert.ThrowsAsync<AIException>(() => client.ChatAsync(new ChatModelRequestDTO("Hello", [], []), default));

        Assert.Equal(503, error.StatusCode);
        Assert.Equal(1, calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RetryAfterBeyondRemainingBudgetReturns503WithoutWaitingOrRetrying(bool useDate)
    {
        var context = new AssistantTestContext();
        var calls = 0;
        using var capacity = new ModelCapacity(Options.Create(context.Settings));
        using var http = Http(new Handler((_, _) =>
        {
            calls++;
            var response = Response("private", HttpStatusCode.ServiceUnavailable);
            response.Headers.RetryAfter = useDate
                ? new System.Net.Http.Headers.RetryConditionHeaderValue(DateTimeOffset.UtcNow.AddMinutes(1))
                : new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromMinutes(1));
            return Task.FromResult(response);
        }));

        var error = await Assert.ThrowsAsync<AIException>(() => Model(http, capacity, context)
            .ChatAsync(new ChatModelRequestDTO("Hello", [], []), default).WaitAsync(TimeSpan.FromSeconds(3)));

        Assert.Equal(503, error.StatusCode);
        Assert.Equal(1, calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RetryAfterWithinBudgetIsHonoredBeforeSecondAttempt(bool useDate)
    {
        var context = new AssistantTestContext();
        var calls = 0;
        var watch = Stopwatch.StartNew();
        TimeSpan? secondAttemptAt = null;
        using var capacity = new ModelCapacity(Options.Create(context.Settings));
        using var http = Http(new Handler((_, _) =>
        {
            calls++;
            if (calls == 1)
            {
                var response = Response("private", HttpStatusCode.ServiceUnavailable);
                response.Headers.RetryAfter = useDate
                    ? new System.Net.Http.Headers.RetryConditionHeaderValue(DateTimeOffset.UtcNow.AddSeconds(1))
                    : new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(1));
                return Task.FromResult(response);
            }

            secondAttemptAt = watch.Elapsed;
            return Task.FromResult(Completed("{\"suggestions\":[]}"));
        }));

        await Model(http, capacity, context).RecommendAsync(new RecommendationModelRequestDTO([], [], 1), default)
            .WaitAsync(TimeSpan.FromSeconds(4));

        Assert.Equal(2, calls);
        Assert.NotNull(secondAttemptAt);
        Assert.True(secondAttemptAt >= TimeSpan.FromMilliseconds(900));
    }

    [Fact]
    public async Task ModelBudgetIsSharedByAttemptsAndBackoffAndReleasesCapacity()
    {
        var context = new AssistantTestContext();
        var settings = ModelSettings();
        settings.RequestTimeoutSeconds = 2;
        settings.RetryDelayMilliseconds = 1000;
        var tokens = new List<CancellationToken>();
        using var capacity = new ModelCapacity(Options.Create(context.Settings));
        using var http = Http(new Handler(async (_, ct) =>
        {
            tokens.Add(ct);
            if (tokens.Count == 1)
            {
                await Task.Delay(600, ct);
                return Response("private", HttpStatusCode.ServiceUnavailable);
            }

            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return Completed("{}");
        }));
        var client = new GeminiModelClient(http, capacity, Options.Create(settings), Options.Create(context.Settings), NullLogger<GeminiModelClient>.Instance);

        var watch = Stopwatch.StartNew();
        var error = await Assert.ThrowsAsync<AIException>(() => client.ChatAsync(new ChatModelRequestDTO("Hello", [], []), default)
            .WaitAsync(TimeSpan.FromSeconds(4)));

        Assert.Equal(504, error.StatusCode);
        Assert.Equal(2, tokens.Count);
        Assert.InRange(watch.Elapsed, TimeSpan.FromSeconds(1.5), TimeSpan.FromSeconds(3));
        Assert.True(capacity.TryEnter(default));
        capacity.Exit();
    }

    [Fact]
    public async Task CallerCancellationDuringBackoffPreventsSecondAttemptAndReleasesCapacity()
    {
        var context = new AssistantTestContext();
        var settings = ModelSettings();
        settings.RetryDelayMilliseconds = 1000;
        var calls = 0;
        using var capacity = new ModelCapacity(Options.Create(context.Settings));
        using var http = Http(new Handler((_, _) =>
        {
            calls++;
            return Task.FromResult(Response("private", HttpStatusCode.ServiceUnavailable));
        }));
        var client = new GeminiModelClient(http, capacity, Options.Create(settings), Options.Create(context.Settings), NullLogger<GeminiModelClient>.Instance);
        using var cancellation = new CancellationTokenSource();
        var pending = client.ChatAsync(new ChatModelRequestDTO("Hello", [], []), cancellation.Token);
        var busy = await Assert.ThrowsAsync<AIException>(() => client.ChatAsync(new ChatModelRequestDTO("Hello", [], []), default));
        cancellation.Cancel();

        Assert.Equal(429, busy.StatusCode);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.Equal(1, calls);
        Assert.True(capacity.TryEnter(default));
        capacity.Exit();
    }

    [Fact]
    public async Task ModelBudgetAlsoCancelsResponseBodyBuffering()
    {
        var context = new AssistantTestContext();
        var settings = ModelSettings();
        settings.RequestTimeoutSeconds = 1;
        using var capacity = new ModelCapacity(Options.Create(context.Settings));
        using var http = Http(new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new DelayedContent()
        })));
        var client = new GeminiModelClient(http, capacity, Options.Create(settings), Options.Create(context.Settings), NullLogger<GeminiModelClient>.Instance);

        var error = await Assert.ThrowsAsync<AIException>(() => client.ChatAsync(new ChatModelRequestDTO("Hello", [], []), default)
            .WaitAsync(TimeSpan.FromSeconds(4)));

        Assert.Equal(504, error.StatusCode);
        Assert.True(capacity.TryEnter(default));
        capacity.Exit();
    }

    private sealed class DelayedContent : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            throw new InvalidOperationException("The HTTP body must receive cancellation.");
        }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken ct)
        {
            return Task.Delay(Timeout.InfiniteTimeSpan, ct);
        }

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
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
            new RestaurantAccessGrpc.RestaurantAccessGrpcClient(invoker), Options.Create(context.Settings), TimeProvider.System, NullLogger<GuestAccessClient>.Instance);
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
            new RestaurantAccessGrpc.RestaurantAccessGrpcClient(invoker), Options.Create(context.Settings), TimeProvider.System, NullLogger<GuestAccessClient>.Instance);
        var error = await Assert.ThrowsAsync<AIException>(() => client.VerifyAsync(context.Guest, default));
        Assert.Equal(503, error.StatusCode);
    }

    [Theory]
    [InlineData("{\"promptTokenCount\":120,\"candidatesTokenCount\":30,\"totalTokenCount\":150}", "120", "30", "150")]
    [InlineData("null", "unknown", "unknown", "unknown")]
    [InlineData("{\"promptTokenCount\":-1,\"candidatesTokenCount\":\"private\",\"totalTokenCount\":1.5}", "unknown", "unknown", "unknown")]
    public async Task UsageLoggingReportsProviderCountsWithoutContentOrInventedZeros(
        string metadata, string expectedPrompt, string expectedOutput, string expectedTotal)
    {
        var context = new AssistantTestContext();
        var logger = new UsageLogger();
        using var activity = new Activity("acceptance").SetIdFormat(ActivityIdFormat.W3C).Start();
        using var capacity = new ModelCapacity(Options.Create(context.Settings));
        using var http = Http(new Handler(async (_, ct) =>
        {
            using var completed = Completed("{\"suggestions\":[]}");
            var json = await completed.Content.ReadAsStringAsync(ct);
            return Response(json[..^1] + ",\"usageMetadata\":" + metadata + "}");
        }));
        var client = new GeminiModelClient(http, capacity, Options.Create(ModelSettings()), Options.Create(context.Settings), logger);

        await client.RecommendAsync(new RecommendationModelRequestDTO([], [], 1), default);

        var message = Assert.Single(logger.Messages, value => value.StartsWith("GeminiUsage ", StringComparison.Ordinal));
        Assert.Contains("TraceId=" + activity.TraceId, message);
        Assert.Contains("Operation=Recommendation", message);
        Assert.Contains("PromptTokens=" + expectedPrompt, message);
        Assert.Contains("OutputTokens=" + expectedOutput, message);
        Assert.Contains("TotalTokens=" + expectedTotal, message);
        Assert.Contains("ThoughtTokens=unknown", message);
        Assert.DoesNotContain("fixture-key", message);
        Assert.DoesNotContain("private", message);
        Assert.DoesNotContain("suggestions", message);
        var timing = Assert.Single(logger.Messages, value => value.StartsWith("AITiming ", StringComparison.Ordinal));
        Assert.Contains("TraceId=" + activity.TraceId, timing);
        Assert.Contains("Stage=Gemini", timing);
        Assert.DoesNotContain("fixture-key", timing);
        Assert.DoesNotContain("private", timing);
    }

    private sealed class UsageLogger : ILogger<GeminiModelClient>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Messages.Add(formatter(state, exception));
        }
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
            ReadinessTimeoutSeconds = 5,
            RequestTimeoutSeconds = 20,
            RetryServiceUnavailable = true,
            RetryDelayMilliseconds = 250
        };
    }

    private static GeminiModelClient Model(HttpClient http, ModelCapacity capacity, AssistantTestContext context)
    {
        return new GeminiModelClient(http, capacity, Options.Create(ModelSettings()), Options.Create(context.Settings), NullLogger<GeminiModelClient>.Instance);
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
