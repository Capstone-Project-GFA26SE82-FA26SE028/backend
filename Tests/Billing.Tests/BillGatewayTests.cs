extern alias Gateway;

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Grpc.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Wire = Gateway::Backend.Contracts.Billing;

namespace Billing.Tests;

public sealed class BillGatewayTests
{
    [Fact]
    public async Task RefreshForwardsBillIdAndEmployeeTokenWithoutClientAmounts()
    {
        var billId = Guid.NewGuid();
        await using var app = await Start(new BillingUnaryInvoker((method, request, options) =>
        {
            Assert.EndsWith("/Refresh", method);
            Assert.Equal(billId.ToString(), Assert.IsType<Wire.BillIdRequest>(request).BillId);
            Assert.Equal("Bearer cashier-session", Assert.Single(options.Headers!).Value);
            return Reply(billId);
        }));
        using var client = app.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "cashier-session");
        using var response = await client.PostAsync($"/api/bills/{billId}/refresh", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task CreateForwardsSessionAndBearerAndPreservesNullCogs()
    {
        var session = Guid.NewGuid();
        var billId = Guid.NewGuid();
        await using var app = await Start(new BillingUnaryInvoker((_, request, options) =>
        {
            Assert.Equal(session.ToString(), Assert.IsType<Wire.CreateBillRequest>(request).TableSessionId);
            Assert.Equal("Bearer cashier-session", Assert.Single(options.Headers!).Value);
            var result = Reply(billId);
            result.Items.Add(new Wire.BillLineInfo
            {
                BillLineId = Guid.NewGuid().ToString(),
                OrderItemRef = Guid.NewGuid().ToString(),
                TableSessionRef = session.ToString(),
                OriginalTableRef = Guid.NewGuid().ToString(),
                ItemSnapshotJson = "{}",
                Quantity = "1",
                UnitPriceSnapshot = "200.25",
                LineTotal = "200.25"
            });
            return result;
        }));
        using var client = app.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "cashier-session");
        using var response = await client.PostAsJsonAsync("/api/bills", new
        {
            TableSessionId = session
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("result").GetProperty("items")[0].GetProperty("cogsSnapshot").ValueKind);
        Assert.Equal(billId.ToString(), json.RootElement.GetProperty("result").GetProperty("billId").GetString());
    }

    [Fact]
    public async Task AnonymousRequestsDoNotReachBilling()
    {
        var calls = 0;
        await using var app = await Start(new BillingUnaryInvoker((_, _, _) =>
        {
            calls++;
            return Reply(Guid.NewGuid());
        }));
        using var client = app.GetTestClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/bills")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync($"/api/bills/{Guid.NewGuid()}")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/bills", new
        {
            TableSessionId = Guid.NewGuid()
        })).StatusCode);
        Assert.Equal(0, calls);
    }

    [Theory]
    [InlineData(StatusCode.PermissionDenied, 403)]
    [InlineData(StatusCode.AlreadyExists, 409)]
    [InlineData(StatusCode.Unavailable, 503)]
    [InlineData(StatusCode.DeadlineExceeded, 504)]
    public async Task ErrorsRemainControlled(StatusCode status, int expected)
    {
        await using var app = await Start(new BillingUnaryInvoker((_, _, _) => throw new RpcException(new Status(status, "private-error"))));
        using var client = app.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "cashier-session");
        using var response = await client.PostAsJsonAsync("/api/bills", new
        {
            TableSessionId = Guid.NewGuid()
        });
        Assert.Equal(expected, (int)response.StatusCode);
        if (expected >= 500)
        {
            Assert.DoesNotContain("private-error", await response.Content.ReadAsStringAsync());
        }
    }

    private static Wire.BillInfo Reply(Guid id)
    {
        return new Wire.BillInfo
        {
            BillId = id.ToString(),
            RestaurantId = Guid.NewGuid().ToString(),
            BillNumber = "TEST",
            Status = "Unpaid",
            Currency = "VND",
            SubTotal = "200.25",
            DiscountAmount = "0",
            VatRateSnapshot = "0",
            ServiceRateSnapshot = "0",
            VatAmount = "0",
            ServiceAmount = "0",
            TotalAmount = "200",
            RoundingAdjustment = "-0.25",
            SourceStatus = "MatchedAtCreation"
        };
    }

    private static async Task<WebApplication> Start(CallInvoker invoker)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddControllers().AddApplicationPart(typeof(Gateway::ApiGateway.ApiAssembly).Assembly);
        builder.Services.Configure<Gateway::ApiGateway.Settings.BillingClientSettings>(settings =>
        {
            settings.TimeoutSeconds = 15;
        });
        builder.Services.AddSingleton(new Wire.BillGrpc.BillGrpcClient(invoker));
        var app = builder.Build();
        app.MapControllers();
        await app.StartAsync();
        return app;
    }
}
