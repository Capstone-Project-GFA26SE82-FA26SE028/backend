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

public sealed class CashMovementGatewayTests
{
    [Theory]
    [InlineData(true, 201)]
    [InlineData(false, 200)]
    public async Task CreateForwardsOnlyManualFieldsAndMapsPerformerSource(bool created, int expected)
    {
        var shiftId = Guid.NewGuid();
        var key = Guid.NewGuid().ToString();
        var movement = Reply(shiftId);
        await using var app = await Start(new BillingUnaryInvoker((method, body, options) =>
        {
            Assert.EndsWith("/Create", method);
            Assert.Equal("Bearer cashier", Assert.Single(options.Headers!).Value);
            var request = Assert.IsType<Wire.CreateCashMovementRequest>(body);
            Assert.Equal(shiftId.ToString(), request.CashShiftId);
            Assert.Equal(key, request.IdempotencyKey);
            Assert.Equal("100", request.Amount);
            Assert.Equal("CashOut", request.Type);
            Assert.Equal("Supplies", request.Reason);
            return new Wire.CashMovementReply
            {
                Movement = movement,
                Created = created
            };
        }));
        using var client = app.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "cashier");
        client.DefaultRequestHeaders.Add("Idempotency-Key", key);
        using var response = await client.PostAsJsonAsync($"/api/cash-shifts/{shiftId}/movements", new
        {
            Type = "CashOut",
            Amount = 100,
            Reason = "Supplies"
        });
        Assert.Equal(expected, (int)response.StatusCode);
        Assert.Equal(created ? $"/api/cash-movements/{movement.CashMovementId}" : null, response.Headers.Location?.ToString());
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var result = json.RootElement.GetProperty("result");
        Assert.Equal(-100, result.GetProperty("signedAmount").GetDecimal());
        Assert.Equal("ShiftOwnerDerived", result.GetProperty("performerSource").GetString());
        Assert.Equal(JsonValueKind.Null, result.GetProperty("paymentId").ValueKind);
    }

    [Fact]
    public async Task ClientCannotAttachPaymentOrInventPerformer()
    {
        await using var app = await Start(new BillingUnaryInvoker((_, _, _) => throw new InvalidOperationException()));
        using var client = app.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "cashier");
        client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString());
        using var response = await client.PostAsJsonAsync($"/api/cash-shifts/{Guid.NewGuid()}/movements", new
        {
            Type = "CashIn",
            Amount = 100,
            Reason = "Funds",
            PaymentId = Guid.NewGuid(),
            PerformedByRef = Guid.NewGuid()
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task LedgerMapsExactBalanceAndNullableHistoricalPerformer()
    {
        var shiftId = Guid.NewGuid();
        var movement = Reply(shiftId);
        movement.ClearPerformedByRef();
        movement.PerformerSource = "Unknown";
        await using var app = await Start(new BillingUnaryInvoker((method, body, _) =>
        {
            Assert.EndsWith("/GetAll", method);
            var request = Assert.IsType<Wire.CashLedgerRequest>(body);
            Assert.Equal(2, request.Page);
            Assert.Equal(5, request.PageSize);
            var reply = new Wire.CashLedgerInfo
            {
                CashShiftId = shiftId.ToString(),
                RestaurantId = movement.RestaurantId,
                Currency = "VND",
                OpeningCash = "1000.50",
                MovementTotal = "-100",
                ExpectedCash = "900.50",
                TotalCount = 6,
                Page = 2,
                PageSize = 5
            };
            reply.Items.Add(movement);
            return reply;
        }));
        using var client = app.GetTestClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync($"/api/cash-shifts/{shiftId}/movements")).StatusCode);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "reader");
        using var response = await client.GetAsync($"/api/cash-shifts/{shiftId}/movements?page=2&pageSize=5");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var result = json.RootElement.GetProperty("result");
        Assert.Equal(900.50m, result.GetProperty("expectedCash").GetDecimal());
        Assert.Equal(JsonValueKind.Null, result.GetProperty("movements").GetProperty("items")[0].GetProperty("performedByRef").ValueKind);
    }

    [Theory]
    [InlineData(StatusCode.PermissionDenied, 403)]
    [InlineData(StatusCode.AlreadyExists, 409)]
    [InlineData(StatusCode.Unavailable, 503)]
    public async Task ErrorsRemainControlled(StatusCode status, int expected)
    {
        await using var app = await Start(new BillingUnaryInvoker((_, _, _) => throw new RpcException(new Status(status, "private-error"))));
        using var client = app.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "reader");
        using var response = await client.GetAsync($"/api/cash-movements/{Guid.NewGuid()}");
        Assert.Equal(expected, (int)response.StatusCode);
        if (expected >= 500)
        {
            Assert.DoesNotContain("private-error", await response.Content.ReadAsStringAsync());
        }
    }

    private static Wire.CashMovementInfo Reply(Guid shiftId)
    {
        return new Wire.CashMovementInfo
        {
            CashMovementId = Guid.NewGuid().ToString(),
            CashShiftId = shiftId.ToString(),
            RestaurantId = Guid.NewGuid().ToString(),
            Type = "CashOut",
            SignedAmount = "-100",
            Reason = "Supplies",
            PostedAt = "2026-10-28T01:00:00Z",
            PerformedByRef = Guid.NewGuid().ToString(),
            PerformerSource = "ShiftOwnerDerived"
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
        builder.Services.AddSingleton(new Wire.CashMovementGrpc.CashMovementGrpcClient(invoker));
        var app = builder.Build();
        app.MapControllers();
        await app.StartAsync();
        return app;
    }
}
