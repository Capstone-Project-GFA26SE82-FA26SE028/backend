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

public sealed class CashHandoverGatewayTests
{
    [Theory]
    [InlineData(true, 201)]
    [InlineData(false, 200)]
    public async Task CreationForwardsOnlyTenderCheckoutAndStableKeyAndPreservesPendingResult(bool created, int status)
    {
        var bill = Guid.NewGuid();
        var checkout = Guid.NewGuid();
        var key = Guid.NewGuid().ToString();
        var value = Reply(bill);
        await using var app = await Start(new BillingUnaryInvoker((method, body, options) =>
        {
            Assert.EndsWith("/Create", method);
            var request = Assert.IsType<Wire.CreateCashHandoverRequest>(body);
            Assert.Equal(bill.ToString(), request.BillId);
            Assert.Equal(checkout.ToString(), request.CheckoutId);
            Assert.Equal("300", request.CashReceived);
            Assert.Equal(key, request.IdempotencyKey);
            Assert.Equal("Bearer waiter", Assert.Single(options.Headers!).Value);
            return new Wire.CashHandoverReply
            {
                Handover = value,
                Created = created
            };
        }));
        using var client = app.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "waiter");
        client.DefaultRequestHeaders.Add("Idempotency-Key", key);
        using var response = await client.PostAsJsonAsync($"/api/bills/{bill}/cash-handovers", new
        {
            CheckoutId = checkout,
            CashReceived = 300,
            WaiterUserRef = Guid.NewGuid(),
            Status = "Paid"
        });
        Assert.Equal(status, (int)response.StatusCode);
        Assert.Equal(created ? $"/api/cash-handovers/{value.PaymentId}" : null, response.Headers.Location?.ToString());
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var result = json.RootElement.GetProperty("result");
        Assert.Equal("PendingHandover", result.GetProperty("paymentStatus").GetString());
        Assert.Equal(JsonValueKind.Null, result.GetProperty("confirmedAt").ValueKind);
        Assert.Equal(JsonValueKind.Null, result.GetProperty("cashierUserRef").ValueKind);
        Assert.Equal(68, result.GetProperty("changeAmount").GetDecimal());
    }

    [Fact]
    public async Task HandoverRoutesRequireEmployeeBearerAndConfirmationForwardsActualAmount()
    {
        var payment = Guid.NewGuid();
        var calls = 0;
        await using var app = await Start(new BillingUnaryInvoker((method, body, _) =>
        {
            calls++;
            Assert.EndsWith("/Confirm", method);
            var request = Assert.IsType<Wire.ConfirmCashHandoverRequest>(body);
            Assert.Equal(payment.ToString(), request.PaymentId);
            Assert.Equal("300", request.ConfirmedAmount);
            return Reply(Guid.NewGuid());
        }));
        using var client = app.GetTestClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync($"/api/cash-handovers/{payment}")).StatusCode);
        Assert.Equal(0, calls);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "cashier");
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync($"/api/cash-handovers/{payment}/confirm", new
        {
            ConfirmedAmount = 300
        })).StatusCode);
        Assert.Equal(1, calls);
    }

    private static Wire.CashHandoverInfo Reply(Guid bill)
    {
        return new Wire.CashHandoverInfo
        {
            PaymentId = Guid.NewGuid().ToString(),
            BillId = bill.ToString(),
            RestaurantId = Guid.NewGuid().ToString(),
            CashRegisterId = Guid.NewGuid().ToString(),
            CashShiftId = Guid.NewGuid().ToString(),
            WaiterUserRef = Guid.NewGuid().ToString(),
            Amount = "232",
            CashReceived = "300",
            ChangeAmount = "68",
            Status = "Pending",
            PaymentStatus = "PendingHandover",
            CollectedAt = DateTimeOffset.UtcNow.ToString("O")
        };
    }

    private static async Task<WebApplication> Start(CallInvoker invoker)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddControllers().AddApplicationPart(typeof(Gateway::ApiGateway.ApiAssembly).Assembly);
        builder.Services.Configure<Gateway::ApiGateway.Settings.BillingClientSettings>(settings => settings.TimeoutSeconds = 15);
        builder.Services.AddSingleton(new Wire.CashHandoverGrpc.CashHandoverGrpcClient(invoker));
        var app = builder.Build();
        app.MapControllers();
        await app.StartAsync();
        return app;
    }
}
