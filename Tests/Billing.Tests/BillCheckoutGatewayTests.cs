extern alias Gateway;

using System.Net;
using System.Text.Json;
using Grpc.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Wire = Gateway::Backend.Contracts.Billing;

namespace Billing.Tests;

public sealed class BillCheckoutGatewayTests
{
    [Fact]
    public async Task GuestCheckoutForwardsOnlyMatchingSessionCookieAndStableRequestId()
    {
        var session = Guid.NewGuid();
        var id = Guid.NewGuid();
        await using var app = await Start(new BillingUnaryInvoker((method, value, _) =>
        {
            Assert.EndsWith("/Request", method);
            var request = Assert.IsType<Wire.BillCheckoutRequest>(value);
            Assert.Equal(session.ToString(), request.TableSessionId);
            Assert.Equal(id.ToString(), request.CheckoutId);
            Assert.Equal("verified-by-backend", request.GuestToken);
            return new Wire.BillCheckoutInfo
            {
                CheckoutId = id.ToString(),
                TableSessionId = session.ToString(),
                Status = "WaitingConfirmation"
            };
        }));
        using var client = app.GetTestClient();
        client.DefaultRequestHeaders.Add("Cookie", $"guest_access_{session:N}=verified-by-backend; guest_access_{Guid.NewGuid():N}=other-session");
        client.DefaultRequestHeaders.Add("Idempotency-Key", id.ToString());
        using var response = await client.PostAsync($"/api/table-sessions/{session}/checkout", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.False(json.RootElement.GetProperty("result").GetProperty("orderingReopened").GetBoolean());
    }

    [Fact]
    public async Task InvalidRequestIdIsRejectedBeforeCallingBackend()
    {
        await using var app = await Start(new BillingUnaryInvoker((_, _, _) => throw new InvalidOperationException("Must not call.")));
        using var client = app.GetTestClient();
        client.DefaultRequestHeaders.Add("Idempotency-Key", "not-a-guid");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync($"/api/table-sessions/{Guid.NewGuid()}/checkout", null)).StatusCode);
    }

    private static async Task<WebApplication> Start(CallInvoker invoker)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddControllers().AddApplicationPart(typeof(Gateway::ApiGateway.ApiAssembly).Assembly);
        builder.Services.Configure<Gateway::ApiGateway.Settings.OrderingClientSettings>(settings => settings.TimeoutSeconds = 15);
        builder.Services.AddSingleton(new Wire.BillCheckoutGrpc.BillCheckoutGrpcClient(invoker));
        var app = builder.Build();
        app.MapControllers();
        await app.StartAsync();
        return app;
    }
}
