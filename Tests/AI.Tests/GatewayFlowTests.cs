extern alias Gateway;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AI.BLL.Interfaces;
using AI.Common.DTOs;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using GatewayAI = Gateway::Backend.Contracts.AI;

namespace AI.Tests;

public sealed class GatewayFlowTests
{
    [Fact]
    public async Task CookieBasedGatewayRoutesRunBothUcWithoutCartWrites()
    {
        var data = new AssistantTestContext();
        await using var ai = await GrpcFlowTests.Start(data);
        await using var gateway = await Start(ai);
        using var client = gateway.GetTestClient();
        client.DefaultRequestHeaders.Add("Cookie", $"guest_access_{data.Guest.TableSessionId:N}={data.Guest.Token}");
        var root = $"/api/ai/table-sessions/{data.Guest.TableSessionId}";
        using var chat = await client.PostAsJsonAsync(root + "/chat", new { message = "Is beef rice available?", history = Array.Empty<object>() });
        Assert.Equal(HttpStatusCode.OK, chat.StatusCode);
        Assert.Equal("no-store", chat.Headers.CacheControl!.ToString());
        using var response = JsonDocument.Parse(await chat.Content.ReadAsStringAsync());
        Assert.Single(response.RootElement.GetProperty("result").GetProperty("items").EnumerateArray());
        using var recommendations = await client.PostAsJsonAsync(root + $"/carts/{data.Cart.CartId}/recommendations", new { limit = 3 });
        Assert.Equal(HttpStatusCode.OK, recommendations.StatusCode);
        Assert.Equal(2, data.CartReads);
    }

    [Fact]
    public async Task MissingQrCookieDoesNotFallBackToBearerOrBodyCredentials()
    {
        var data = new AssistantTestContext();
        await using var ai = await GrpcFlowTests.Start(data);
        await using var gateway = await Start(ai);
        using var client = gateway.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "staff-token");
        using var result = await client.PostAsJsonAsync($"/api/ai/table-sessions/{data.Guest.TableSessionId}/chat", new
        {
            message = "Hello",
            guestToken = data.Guest.Token
        });
        Assert.Equal(HttpStatusCode.Unauthorized, result.StatusCode);
        Assert.Equal(0, data.ModelCalls);
    }

    [Fact]
    public async Task UnavailableDependencyReturnsControlled503ThroughGateway()
    {
        var data = new AssistantTestContext();
        await using var ai = await GrpcFlowTests.Start(data, services => services.AddSingleton<IGuestAccessClient>(new UnavailableGuest()));
        await using var gateway = await Start(ai);
        using var client = gateway.GetTestClient();
        client.DefaultRequestHeaders.Add("Cookie", $"guest_access_{data.Guest.TableSessionId:N}={data.Guest.Token}");
        using var result = await client.PostAsJsonAsync($"/api/ai/table-sessions/{data.Guest.TableSessionId}/chat", new { message = "Hello" });
        Assert.Equal(HttpStatusCode.ServiceUnavailable, result.StatusCode);
        Assert.DoesNotContain("private", await result.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task GuestRestaurantChangingDuringInferenceRejectsTheReply()
    {
        var data = new AssistantTestContext();
        data.AfterModel = () => data.RestaurantId = Guid.NewGuid();
        await using var ai = await GrpcFlowTests.Start(data);
        await using var gateway = await Start(ai);
        using var client = gateway.GetTestClient();
        client.DefaultRequestHeaders.Add("Cookie", $"guest_access_{data.Guest.TableSessionId:N}={data.Guest.Token}");

        using var result = await client.PostAsJsonAsync($"/api/ai/table-sessions/{data.Guest.TableSessionId}/chat",
            new { message = "Is beef rice available?" });
        Assert.Equal(HttpStatusCode.Forbidden, result.StatusCode);
        Assert.Equal(1, data.ModelCalls);
        Assert.DoesNotContain("Beef rice", await result.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task NullHistoryAndOversizedRequestAreRejectedBeforeModel()
    {
        var data = new AssistantTestContext();
        await using var ai = await GrpcFlowTests.Start(data);
        await using var gateway = await Start(ai);
        using var client = gateway.GetTestClient();
        using var result = await client.PostAsJsonAsync($"/api/ai/table-sessions/{data.Guest.TableSessionId}/chat", new { message = new string('x', 1001), history = (object?)null });
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
    }

    [Fact]
    public void EnvironmentOverridesLocalFileWhichOverridesSharedConfiguration()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ai-config-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, "{\"Gemini\":{\"ApiKey\":\"local-file-test-key\"}}");
            var key = "AI_CONFIG_TEST_Gemini__ApiKey";
            Environment.SetEnvironmentVariable(key, "environment-test-key");
            try
            {
                var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Gemini:ApiKey"] = "shared-test-key"
                }).AddJsonFile(path).AddEnvironmentVariables("AI_CONFIG_TEST_").Build();
                Assert.Equal("environment-test-key", config["Gemini:ApiKey"]);
            }
            finally
            {
                Environment.SetEnvironmentVariable(key, null);
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static async Task<WebApplication> Start(WebApplication ai)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddControllers().AddApplicationPart(typeof(Gateway::ApiGateway.ApiAssembly).Assembly);
        builder.Services.Configure<Gateway::ApiGateway.Settings.AIClientSettings>(settings =>
        {
            settings.TimeoutSeconds = 90;
        });
        var channel = GrpcFlowTests.Channel(ai);
        builder.Services.AddSingleton(channel);
        builder.Services.AddSingleton(new GatewayAI.ChatGrpc.ChatGrpcClient(channel));
        builder.Services.AddSingleton(new GatewayAI.RecommendationGrpc.RecommendationGrpcClient(channel));
        var app = builder.Build();
        app.MapControllers();
        await app.StartAsync();
        return app;
    }

    private sealed class UnavailableGuest : IGuestAccessClient
    {
        public Task<VerifiedGuest> VerifyAsync(GuestContext guest, CancellationToken ct)
        {
            throw new Grpc.Core.RpcException(new Grpc.Core.Status(Grpc.Core.StatusCode.Unavailable, "private dependency details"));
        }
    }
}
