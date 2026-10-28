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

public sealed class CashPaymentGatewayTests
{
    [Theory]
    [InlineData(true, 201)]
    [InlineData(false, 200)]
    public async Task CashReceiptForwardsIdentityAndKeyAndPreservesAmounts(bool created, int status)
    {
        var billId = Guid.NewGuid();
        var checkoutId = Guid.NewGuid();
        var shiftId = Guid.NewGuid();
        var key = Guid.NewGuid().ToString();
        var receipt = Receipt(billId);
        await using var app = await Start(new BillingUnaryInvoker((method, body, options) =>
        {
            Assert.EndsWith("/PayCash", method);
            var request = Assert.IsType<Wire.PayCashRequest>(body);
            Assert.Equal(billId.ToString(), request.BillId);
            Assert.Equal(checkoutId.ToString(), request.CheckoutId);
            Assert.Equal(shiftId.ToString(), request.CashShiftId);
            Assert.Equal("300", request.CashReceived);
            Assert.Equal(key, request.IdempotencyKey);
            Assert.Equal("Bearer cashier-session", Assert.Single(options.Headers!).Value);
            return new Wire.CashPaymentReply
            {
                Payment = receipt,
                Created = created
            };
        }));
        using var client = app.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "cashier-session");
        client.DefaultRequestHeaders.Add("Idempotency-Key", key);
        using var response = await client.PostAsJsonAsync($"/api/bills/{billId}/payments/cash", new
        {
            CheckoutId = checkoutId,
            CashShiftId = shiftId,
            CashReceived = 300
        });
        Assert.Equal(status, (int)response.StatusCode);
        Assert.Equal(created ? $"/api/payments/{receipt.PaymentId}" : null, response.Headers.Location?.ToString());
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var result = json.RootElement.GetProperty("result");
        Assert.Equal(232m, result.GetProperty("amount").GetDecimal());
        Assert.Equal(68m, result.GetProperty("changeAmount").GetDecimal());
        Assert.Equal("Paid", result.GetProperty("status").GetString());
    }

    [Fact]
    public async Task ReceiptLookupRequiresBearerAndForwardsPaymentId()
    {
        var payment = Receipt(Guid.NewGuid());
        var calls = 0;
        await using var app = await Start(new BillingUnaryInvoker((method, body, options) =>
        {
            calls++;
            Assert.EndsWith("/GetById", method);
            Assert.Equal(payment.PaymentId, Assert.IsType<Wire.PaymentIdRequest>(body).PaymentId);
            Assert.Equal("Bearer employee", Assert.Single(options.Headers!).Value);
            return payment;
        }));
        using var client = app.GetTestClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync($"/api/payments/{payment.PaymentId}")).StatusCode);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "employee");
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/payments/{payment.PaymentId}")).StatusCode);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task ClientCannotSupplyBillTotalOrCashierIdentity()
    {
        var calls = 0;
        await using var app = await Start(new BillingUnaryInvoker((_, _, _) =>
        {
            calls++;
            throw new InvalidOperationException();
        }));
        using var client = app.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "employee");
        client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString());
        using var response = await client.PostAsJsonAsync($"/api/bills/{Guid.NewGuid()}/payments/cash", new
        {
            CheckoutId = Guid.NewGuid(),
            CashShiftId = Guid.NewGuid(),
            CashReceived = 300,
            Amount = 1,
            ConfirmedByRef = Guid.NewGuid()
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, calls);
    }

    [Theory]
    [InlineData(StatusCode.PermissionDenied, 403)]
    [InlineData(StatusCode.FailedPrecondition, 409)]
    [InlineData(StatusCode.Unavailable, 503)]
    public async Task CashPaymentErrorsRemainControlled(StatusCode error, int expected)
    {
        await using var app = await Start(new BillingUnaryInvoker((_, _, _) => throw new RpcException(new Status(error, "private-error"))));
        using var client = app.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "employee");
        client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString());
        using var response = await client.PostAsJsonAsync($"/api/bills/{Guid.NewGuid()}/payments/cash", new
        {
            CheckoutId = Guid.NewGuid(),
            CashShiftId = Guid.NewGuid(),
            CashReceived = 300
        });
        Assert.Equal(expected, (int)response.StatusCode);
        if (expected >= 500)
        {
            Assert.DoesNotContain("private-error", await response.Content.ReadAsStringAsync());
        }
    }

    private static Wire.PaymentInfo Receipt(Guid billId)
    {
        return new Wire.PaymentInfo
        {
            PaymentId = Guid.NewGuid().ToString(),
            BillId = billId.ToString(),
            BillNumber = "TEST",
            RestaurantId = Guid.NewGuid().ToString(),
            CheckoutId = Guid.NewGuid().ToString(),
            CashShiftId = Guid.NewGuid().ToString(),
            CashRegisterId = Guid.NewGuid().ToString(),
            MethodCode = "Cash",
            Status = "Paid",
            Currency = "VND",
            Amount = "232",
            CashReceived = "300",
            ChangeAmount = "68",
            ConfirmedByRef = Guid.NewGuid().ToString(),
            PaidAt = "2026-10-28T03:00:00Z",
            CashMovementId = Guid.NewGuid().ToString()
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
        builder.Services.AddSingleton(new Wire.PaymentGrpc.PaymentGrpcClient(invoker));
        var app = builder.Build();
        app.MapControllers();
        await app.StartAsync();
        return app;
    }
}
