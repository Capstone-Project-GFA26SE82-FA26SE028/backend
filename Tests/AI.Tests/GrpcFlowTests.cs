using AI.API.Extensions;
using AI.API.GrpcServices;
using AI.BLL.Interfaces;
using AI.Common.DTOs;
using Backend.Contracts.AI;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace AI.Tests;

public sealed class GrpcFlowTests
{
    [Fact]
    public async Task AiGrpcRunsChatAndRecommendationsWithVerifiedGuestAndFreshCards()
    {
        var data = new AssistantTestContext();
        await using var host = await Start(data);
        using var channel = Channel(host);
        var chat = await new ChatGrpc.ChatGrpcClient(channel).ChatAsync(Question(data));
        Assert.Single(chat.Items);
        Assert.NotEmpty(chat.Items[0].PriceId);
        var response = await new RecommendationGrpc.RecommendationGrpcClient(channel).RecommendAsync(new RecommendationRequest
        {
            Access = Access(data),
            CartId = data.Cart.CartId.ToString(),
            Limit = 3
        });
        Assert.Equal(2, response.Items.Count);
        Assert.DoesNotContain(response.Items, item => item.Item.ItemId == data.Menu[0].ItemId.ToString());
    }

    [Fact]
    public async Task InvalidAndRevokedQrHaveUnauthenticatedStatus()
    {
        var data = new AssistantTestContext { DenyGuest = true };
        await using var host = await Start(data);
        using var channel = Channel(host);
        var client = new ChatGrpc.ChatGrpcClient(channel);
        var error = await Assert.ThrowsAsync<RpcException>(() => client.ChatAsync(Question(data)).ResponseAsync);
        Assert.Equal(StatusCode.Unauthenticated, error.StatusCode);
        var missing = await Assert.ThrowsAsync<RpcException>(() => client.ChatAsync(new ChatRequest { Message = "Hello" }).ResponseAsync);
        Assert.Equal(StatusCode.Unauthenticated, missing.StatusCode);
    }

    [Fact]
    public async Task TotalBudgetIncludesDataReadAndModelTime()
    {
        var data = new AssistantTestContext();
        var dependencies = new BudgetDependencies(data);
        await using var host = await Start(data, services =>
        {
            services.AddSingleton<IGuestAccessClient>(dependencies);
            services.AddSingleton<IAssistantModelClient>(dependencies);
        }, ("AI:RequestTimeoutSeconds", "1"));
        using var channel = Channel(host);
        // Observe the shared request budget, not client/JIT latency under parallel CI load.
        var response = new ChatGrpc.ChatGrpcClient(channel).ChatAsync(Question(data)).ResponseAsync;
        var error = await Assert.ThrowsAsync<RpcException>(() => response.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Equal(StatusCode.DeadlineExceeded, error.StatusCode);
        Assert.True(dependencies.GuestCompleted);
        Assert.True(dependencies.ModelStarted);
        Assert.True(dependencies.ModelCancelled);
        Assert.True(dependencies.GuestCancellation.CanBeCanceled);
        Assert.Equal(dependencies.GuestCancellation, dependencies.ModelCancellation);
        Assert.True(dependencies.GuestCancellation.IsCancellationRequested);
    }

    [Theory]
    [InlineData(StatusCode.PermissionDenied)]
    [InlineData(StatusCode.Unauthenticated)]
    [InlineData(StatusCode.Unavailable)]
    public async Task DependencyFailuresDoNotLeakPrivateDetails(StatusCode status)
    {
        var data = new AssistantTestContext();
        await using var host = await Start(data, services => services.AddSingleton<IGuestAccessClient>(new FailedGuest(status)));
        using var channel = Channel(host);
        var error = await Assert.ThrowsAsync<RpcException>(() => new ChatGrpc.ChatGrpcClient(channel).ChatAsync(Question(data)).ResponseAsync);
        Assert.Equal(status, error.StatusCode);
        Assert.DoesNotContain("secret", error.Status.Detail);
    }

    public static async Task<WebApplication> Start(AssistantTestContext data, Action<IServiceCollection>? customize = null, params (string Key, string Value)[] changes)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddAssistant(ConfigurationTests.Configuration(changes));
        builder.Services.AddSingleton<IGuestAccessClient>(data);
        builder.Services.AddSingleton<ICatalogMenuClient>(data);
        builder.Services.AddSingleton<ICartClient>(data);
        builder.Services.AddSingleton<IAssistantModelClient>(data);
        customize?.Invoke(builder.Services);
        var host = builder.Build();
        host.MapGrpcService<ChatGrpcService>();
        host.MapGrpcService<RecommendationGrpcService>();
        await host.StartAsync();
        return host;
    }

    public static GrpcChannel Channel(WebApplication host)
    {
        return GrpcChannel.ForAddress("http://localhost", new GrpcChannelOptions
        {
            HttpHandler = host.GetTestServer().CreateHandler()
        });
    }

    public static GuestAccess Access(AssistantTestContext data)
    {
        return new GuestAccess
        {
            TableSessionId = data.Guest.TableSessionId.ToString(),
            GuestToken = data.Guest.Token
        };
    }

    public static ChatRequest Question(AssistantTestContext data)
    {
        return new ChatRequest
        {
            Access = Access(data),
            Message = "Is beef rice available?"
        };
    }

    private sealed class FailedGuest(StatusCode status) : IGuestAccessClient
    {
        public Task<VerifiedGuest> VerifyAsync(GuestContext guest, CancellationToken ct)
        {
            throw new RpcException(new Status(status, "secret private dependency details"));
        }
    }

    private sealed class BudgetDependencies(AssistantTestContext data) : IGuestAccessClient, IAssistantModelClient
    {
        public bool GuestCompleted { get; private set; }
        public bool ModelStarted { get; private set; }
        public bool ModelCancelled { get; private set; }
        public CancellationToken GuestCancellation { get; private set; }
        public CancellationToken ModelCancellation { get; private set; }

        public async Task<VerifiedGuest> VerifyAsync(GuestContext guest, CancellationToken ct)
        {
            GuestCancellation = ct;
            var verified = await data.VerifyAsync(guest, ct);
            GuestCompleted = true;
            return verified;
        }

        public async Task<ChatPlanDTO> ChatAsync(ChatModelRequestDTO request, CancellationToken ct)
        {
            ModelCancellation = ct;
            ModelStarted = true;
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                ModelCancelled = true;
                throw;
            }

            return await data.ChatAsync(request, ct);
        }

        public Task<IReadOnlyList<ModelSuggestionDTO>> RecommendAsync(RecommendationModelRequestDTO request, CancellationToken ct)
        {
            return data.RecommendAsync(request, ct);
        }

        public Task<bool> IsReadyAsync(CancellationToken ct)
        {
            return Task.FromResult(true);
        }
    }
}
