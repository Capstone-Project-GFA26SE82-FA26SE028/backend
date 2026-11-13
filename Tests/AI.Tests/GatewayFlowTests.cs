extern alias Gateway;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AI.API.Clients;
using AI.BLL.Interfaces;
using AI.Common.DTOs;
using AI.Common.Settings;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using GatewayAI = Gateway::Backend.Contracts.AI;

namespace AI.Tests;

public sealed class GatewayFlowTests
{
    [Fact]
    public async Task CartRecommendationsReturnVietnameseMessagesAndKeepCatalogCards()
    {
        var data = new AssistantTestContext();
        await using var ai = await GrpcFlowTests.Start(data);
        await using var gateway = await Start(ai);
        using var client = gateway.GetTestClient();
        client.DefaultRequestHeaders.Add("Cookie", $"guest_access_{data.Guest.TableSessionId:N}={data.Guest.Token}");

        using var response = await client.PostAsJsonAsync($"/api/ai/table-sessions/{data.Guest.TableSessionId}/carts/{data.Cart.CartId}/recommendations", new { limit = 3 });
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Lấy gợi ý món thành công.", json.RootElement.GetProperty("message").GetString());
        var result = json.RootElement.GetProperty("result");
        Assert.Equal("Đã có gợi ý cho bạn. Vui lòng chọn các tùy chọn bắt buộc trước khi thêm món vào giỏ hàng.", result.GetProperty("message").GetString());
        Assert.Equal(2, result.GetProperty("items").GetArrayLength());
        foreach (var suggestion in result.GetProperty("items").EnumerateArray())
        {
            Assert.Equal("Món ăn kèm được gợi ý cho các món bạn đã chọn.", suggestion.GetProperty("reason").GetString());
            var card = suggestion.GetProperty("item");
            var catalogItem = data.Menu.Single(item => item.ItemId.ToString() == card.GetProperty("itemId").GetString());
            Assert.Equal(catalogItem.ItemName, card.GetProperty("itemName").GetString());
            Assert.Equal(catalogItem.Price, card.GetProperty("price").GetDecimal());
            Assert.True(card.GetProperty("canOrder").GetBoolean());
            Assert.DoesNotContain(data.Cart.Items, line => line.ItemId == catalogItem.ItemId);
        }
        Assert.Equal(1, data.ModelCalls);
    }

    [Fact]
    public async Task CartRecommendationsRequireQrAndReturnVietnameseAccessError()
    {
        var data = new AssistantTestContext();
        await using var ai = await GrpcFlowTests.Start(data);
        await using var gateway = await Start(ai);
        using var client = gateway.GetTestClient();

        using var response = await client.PostAsJsonAsync($"/api/ai/table-sessions/{data.Guest.TableSessionId}/carts/{data.Cart.CartId}/recommendations", new { limit = 3 });
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Phiên khách tại bàn không hợp lệ hoặc đã hết hạn. Vui lòng quét mã QR và tham gia bàn lại.", json.RootElement.GetProperty("message").GetString());
        Assert.Equal(0, data.ModelCalls);
    }

    [Theory]
    [InlineData(400, "Thông tin yêu cầu gợi ý món không hợp lệ. Vui lòng kiểm tra giỏ hàng và số món gợi ý.")]
    [InlineData(403, "Bạn không có quyền xem gợi ý cho giỏ hàng này.")]
    [InlineData(404, "Không tìm thấy giỏ hàng hoặc dữ liệu món được yêu cầu.")]
    [InlineData(409, "Giỏ hàng hoặc trạng thái món đã thay đổi. Vui lòng tải lại giỏ hàng và thử lại.")]
    [InlineData(413, "Yêu cầu gợi ý món quá lớn. Vui lòng giảm dữ liệu gửi lên.")]
    [InlineData(429, "Trợ lý đang bận. Vui lòng thử lại sau ít phút.")]
    [InlineData(503, "Trợ lý hoặc dịch vụ cần thiết chưa sẵn sàng. Vui lòng thử lại hoặc hỏi nhân viên.")]
    [InlineData(504, "Yêu cầu gợi ý món đã quá thời gian xử lý. Vui lòng thử lại.")]
    [InlineData(502, "Trợ lý chưa thể tạo gợi ý món đã được xác minh. Vui lòng thử lại.")]
    public async Task CartRecommendationErrorsStayVietnameseAndKeepHttpStatus(int status, string message)
    {
        var data = new AssistantTestContext
        {
            AfterModel = () => throw new AI.Common.Exceptions.AIException(status, "Private English provider detail")
        };
        await using var ai = await GrpcFlowTests.Start(data);
        await using var gateway = await Start(ai);
        using var client = gateway.GetTestClient();
        client.DefaultRequestHeaders.Add("Cookie", $"guest_access_{data.Guest.TableSessionId:N}={data.Guest.Token}");

        using var response = await client.PostAsJsonAsync($"/api/ai/table-sessions/{data.Guest.TableSessionId}/carts/{data.Cart.CartId}/recommendations", new { limit = 3 });
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(status, (int)response.StatusCode);
        Assert.Equal(message, json.RootElement.GetProperty("message").GetString());
        Assert.Equal(1, data.ModelCalls);
    }

    [Theory]
    [InlineData("{\"limit\":0}")]
    [InlineData("{\"limit\":4}")]
    [InlineData("{\"limit\":\"many\"}")]
    [InlineData("null")]
    public async Task CartRecommendationValidationErrorsAreVietnameseBeforeActionRuns(string body)
    {
        var data = new AssistantTestContext();
        await using var ai = await GrpcFlowTests.Start(data);
        await using var gateway = await Start(ai);
        using var client = gateway.GetTestClient();
        using var request = new StringContent(body, System.Text.Encoding.UTF8, "application/json");

        using var response = await client.PostAsync($"/api/ai/table-sessions/{data.Guest.TableSessionId}/carts/{data.Cart.CartId}/recommendations", request);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Dữ liệu yêu cầu không hợp lệ.", json.RootElement.GetProperty("title").GetString());
        Assert.Equal(0, data.ModelCalls);
        Assert.Equal(0, data.CartReads);
    }


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
    public async Task MissingKeyReadinessDoesNotCallProvider()
    {
        var data = new AssistantTestContext();
        using var capacity = new AssistantCapacity(Options.Create(data.Settings));
        using var http = new HttpClient();
        var settings = ClientContractTests.ModelSettings();
        settings.ApiKey = string.Empty;
        var model = new GeminiModelClient(http, capacity, Options.Create(settings), Options.Create(data.Settings));
        Assert.False(await model.IsReadyAsync(default));
    }

    [Fact]
    public void EnvironmentOverridesLocalFileWhichOverridesSharedConfiguration()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ai-config-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, "{\"Gemini\":{\"Endpoint\":\"https://local-file\"}}");
            var key = "AI_CONFIG_TEST_Gemini__Endpoint";
            Environment.SetEnvironmentVariable(key, "https://environment");
            try
            {
                var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Gemini:Endpoint"] = "https://shared"
                }).AddJsonFile(path).AddEnvironmentVariables("AI_CONFIG_TEST_").Build();
                Assert.Equal("https://environment", config["Gemini:Endpoint"]);
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

    internal static async Task<WebApplication> Start(WebApplication ai)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        Gateway::ApiGateway.Extensions.VietnameseValidationExtensions.AddVietnameseValidation(
            builder.Services.AddControllers().AddApplicationPart(typeof(Gateway::ApiGateway.ApiAssembly).Assembly));
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
