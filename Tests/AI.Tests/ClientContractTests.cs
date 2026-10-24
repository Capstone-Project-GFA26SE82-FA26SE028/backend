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
    public async Task ExistingGuestRpcReceivesQrCredentialsAndTenSecondDeadline()
    {
        var context = new AssistantTestContext();
        var invoker = new UnaryInvoker((method, request, options) =>
        {
            Assert.Equal("/restaurant.GuestAccessGrpc/GetMe", method);
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
        var client = new GuestAccessClient(new GuestAccessGrpc.GuestAccessGrpcClient(invoker), Options.Create(context.Settings), TimeProvider.System);
        await client.VerifyAsync(context.Guest, default);
    }

    [Fact]
    public async Task CartClientOnlyReadsExistingCartAndStripsPrivateQuote()
    {
        var context = new AssistantTestContext();
        var invoker = new UnaryInvoker((method, request, options) =>
        {
            Assert.Equal("/ordering.CartGrpc/GetById", method);
            var access = Assert.IsType<CartIdRequest>(request);
            Assert.Equal(context.Guest.Token, access.Access.GuestToken);
            return new CartInfo
            {
                CartId = access.CartId,
                TableSessionId = access.Access.TableSessionId,
                Version = "actual-version",
                ExpiresAt = DateTimeOffset.UtcNow.AddHours(1).ToString("O")
            };
        });
        var client = new OrderingCartClient(new CartGrpc.CartGrpcClient(invoker), Options.Create(context.Settings));
        var result = await client.GetByIdAsync(context.Guest, context.Cart.CartId, default);
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
        var error = await Assert.ThrowsAsync<AIException>(() => client.GetByIdAsync(context.Guest, "DineIn", context.Menu[0].ItemId, default));
        Assert.Equal(503, error.StatusCode);
    }

    [Theory]
    [InlineData("not-json", 502)]
    [InlineData("{\"done\":false}", 502)]
    [InlineData("{\"done\":true,\"done_reason\":\"length\"}", 502)]
    public async Task InvalidOllamaResponsesFailWithoutReturningModelText(string body, int code)
    {
        var context = new AssistantTestContext();
        using var capacity = new OllamaCapacity(Options.Create(context.Settings));
        using var http = Http(new Handler((_, _) => Task.FromResult(Response(body))));
        var client = Model(http, capacity, context);
        var error = await Assert.ThrowsAsync<AIException>(() => client.ChatAsync(new ChatModelRequestDTO("Hello", [], []), default));
        Assert.Equal(code, error.StatusCode);
        Assert.True(capacity.TryEnter(default));
        capacity.Exit();
    }

    [Fact]
    public async Task ModelRequestUsesConfiguredParametersAndNoGuestCredentials()
    {
        var context = new AssistantTestContext();
        using var capacity = new OllamaCapacity(Options.Create(context.Settings));
        using var http = Http(new Handler(async (request, ct) =>
        {
            var body = await request.Content!.ReadAsStringAsync(ct);
            Assert.DoesNotContain(context.Guest.Token, body);
            Assert.DoesNotContain(context.Guest.TableSessionId.ToString(), body);
            using var json = JsonDocument.Parse(body);
            Assert.Equal("qwen3:4b-instruct-2507-q4_K_M", json.RootElement.GetProperty("model").GetString());
            Assert.Equal(2048, json.RootElement.GetProperty("options").GetProperty("num_ctx").GetInt32());
            Assert.False(json.RootElement.GetProperty("stream").GetBoolean());
            return Response("{\"done\":true,\"done_reason\":\"stop\",\"message\":{\"content\":\"{\\\"intent\\\":\\\"out_of_scope\\\",\\\"informationKeys\\\":[],\\\"recommendationKeys\\\":[],\\\"maximumPrice\\\":null}\"}}");
        }));
        var plan = await Model(http, capacity, context).ChatAsync(new ChatModelRequestDTO("Hello", [], []), default);
        Assert.Equal("out_of_scope", plan.Intent);
        Assert.Equal(Timeout.InfiniteTimeSpan, http.Timeout);
    }

    [Fact]
    public async Task BusyModelReturnsImmediatelyWithoutQueueingAndCancellationReleasesSlot()
    {
        var context = new AssistantTestContext();
        using var capacity = new OllamaCapacity(Options.Create(context.Settings));
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
    public async Task OllamaNetworkFailureHasExplicitUnavailableError()
    {
        var context = new AssistantTestContext();
        using var capacity = new OllamaCapacity(Options.Create(context.Settings));
        using var http = Http(new Handler((_, _) => throw new HttpRequestException("private details")));
        var error = await Assert.ThrowsAsync<AIException>(() => Model(http, capacity, context).ChatAsync(new ChatModelRequestDTO("Hello", [], []), default));
        Assert.Equal(503, error.StatusCode);
        Assert.DoesNotContain("private", error.Message);
    }

    [Fact]
    public async Task PromptLimitRejectsOversizedInputBeforeContactingModel()
    {
        var context = new AssistantTestContext();
        using var capacity = new OllamaCapacity(Options.Create(context.Settings));
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
        var client = new GuestAccessClient(new GuestAccessGrpc.GuestAccessGrpcClient(invoker), Options.Create(context.Settings), TimeProvider.System);
        var error = await Assert.ThrowsAsync<AIException>(() => client.VerifyAsync(context.Guest, default));
        Assert.Equal(401, error.StatusCode);
    }

    public static OllamaSettings ModelSettings()
    {
        return new OllamaSettings
        {
            Endpoint = "http://127.0.0.1:11434",
            Model = "qwen3:4b-instruct-2507-q4_K_M",
            ContextLength = 2048,
            MaximumOutputTokens = 256,
            MaximumPromptCharacters = 5000,
            KeepAliveMinutes = 5
        };
    }

    private static OllamaModelClient Model(HttpClient http, OllamaCapacity capacity, AssistantTestContext context)
    {
        return new OllamaModelClient(http, capacity, Options.Create(ModelSettings()), Options.Create(context.Settings));
    }

    private static HttpClient Http(HttpMessageHandler handler)
    {
        return new HttpClient(handler)
        {
            BaseAddress = new Uri("http://127.0.0.1:11434/"),
            Timeout = Timeout.InfiniteTimeSpan,
            MaxResponseContentBufferSize = 1024 * 1024
        };
    }

    private static HttpResponseMessage Response(string body)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
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
